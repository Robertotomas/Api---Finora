using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Finora.Application.DTOs.Dashboard;
using Finora.Application.DTOs.Reports;
using Finora.Application.Interfaces;
using Finora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace Finora.Infrastructure.Services;

public class MonthlyReportGenerationService : IMonthlyReportGenerationService
{
    private readonly IDashboardService _dashboardService;
    private readonly IMonthlyReportRepository _monthlyReportRepository;
    private readonly IHouseholdRepository _householdRepository;
    private readonly IUserRepository _userRepository;
    private readonly ISubscriptionService _subscriptionService;
    private readonly IFileStorageService _fileStorage;
    private readonly ILogger<MonthlyReportGenerationService> _logger;

    private static readonly JsonSerializerOptions JsonHtmlSafe = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public MonthlyReportGenerationService(
        IDashboardService dashboardService,
        IMonthlyReportRepository monthlyReportRepository,
        IHouseholdRepository householdRepository,
        IUserRepository userRepository,
        ISubscriptionService subscriptionService,
        IFileStorageService fileStorage,
        ILogger<MonthlyReportGenerationService> logger)
    {
        _dashboardService = dashboardService;
        _monthlyReportRepository = monthlyReportRepository;
        _householdRepository = householdRepository;
        _userRepository = userRepository;
        _subscriptionService = subscriptionService;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    public async Task GenerateDueReportsAsync(CancellationToken cancellationToken = default)
    {
        var householdIds = await _householdRepository.GetAllHouseholdIdsAsync(cancellationToken);
        _logger.LogInformation("Report generation: found {Count} households", householdIds.Count);

        foreach (var householdId in householdIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await _subscriptionService.CanAccessMonthlyReportsAsync(householdId, cancellationToken))
            {
                _logger.LogInformation("Household {Id}: no report access, skipping", householdId);
                continue;
            }

            var users = await _userRepository.GetByHouseholdIdAsync(householdId, cancellationToken);
            if (users.Count == 0)
                continue;

            var anchorUser = users.OrderBy(u => u.CreatedAt).First();
            var tz = ResolveTimeZone(anchorUser.TimeZoneId);
            var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);

            // Check all past months since the household was created
            var createdAt = anchorUser.CreatedAt;
            var startYear = createdAt.Year;
            var startMonth = createdAt.Month;

            // Iterate from the month after creation up to last month
            var currentYear = localNow.Year;
            var currentMonth = localNow.Month;

            _logger.LogInformation("Household {Id}: created {Year}-{Month:00}, checking up to {CurYear}-{CurMonth:00}",
                householdId, startYear, startMonth, currentYear, currentMonth);

            var checkYear = startYear;
            var checkMonth = startMonth;

            while (true)
            {
                // Advance to next month
                checkMonth++;
                if (checkMonth > 12)
                {
                    checkMonth = 1;
                    checkYear++;
                }

                // Stop if we've reached the current month (can't generate for current/future months)
                if (checkYear > currentYear || (checkYear == currentYear && checkMonth >= currentMonth))
                    break;

                cancellationToken.ThrowIfCancellationRequested();

                // reportYear/reportMonth = the month we want to generate the report for
                var reportYear = checkYear;
                var reportMonth = checkMonth;

                if (await _monthlyReportRepository.ExistsAsync(householdId, reportYear, reportMonth, cancellationToken))
                {
                    _logger.LogInformation("Report {Year}-{Month:00} already exists, skipping", reportYear, reportMonth);
                    continue;
                }

                _logger.LogInformation("Generating report for {Year}-{Month:00}...", reportYear, reportMonth);

                try
                {
                    await GenerateForHouseholdMonthAsync(householdId, anchorUser.Id, reportYear, reportMonth, cancellationToken);
                    _logger.LogInformation("Generated report for household {HouseholdId}: {Year}-{Month:00}", householdId, reportYear, reportMonth);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to generate report for household {HouseholdId}: {Year}-{Month:00}. Skipping to next.", householdId, reportYear, reportMonth);
                }
            }
        }
    }

    public async Task<Guid?> GenerateForHouseholdMonthAsync(
        Guid householdId,
        Guid actingUserId,
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        if (year < 2000 || year > 2100 || month < 1 || month > 12)
            return null;

        if (!await _subscriptionService.CanAccessMonthlyReportsAsync(householdId, cancellationToken))
            return null;

        if (await _monthlyReportRepository.ExistsAsync(householdId, year, month, cancellationToken))
        {
            var existing = (await _monthlyReportRepository.ListByHouseholdAsync(householdId, year, month, cancellationToken))
                .FirstOrDefault();
            return existing?.Id;
        }

        var dashboard = await _dashboardService.GetDashboardAsync(
            householdId,
            actingUserId,
            year,
            month,
            trendMonths: 120,
            cancellationToken);

        // Filter data to only include information up to the report month
        dashboard = CapDashboardToMonth(dashboard, year, month);

        var fileName = $"{year}-{month:00}.pdf";
        var relativePath = $"reports/{householdId:N}/{fileName}";

        var html = BuildHtmlDocument(dashboard, year, month);
        var pdfBytes = await RenderPdfAsync(html, cancellationToken);

        await _fileStorage.UploadAsync(relativePath, pdfBytes, "application/pdf", cancellationToken);

        var entity = new MonthlyReport
        {
            Id = Guid.NewGuid(),
            HouseholdId = householdId,
            Year = year,
            Month = month,
            GeneratedAt = DateTime.UtcNow,
            FileRelativePath = relativePath,
            FileSizeBytes = pdfBytes.LongLength,
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            await _monthlyReportRepository.AddAsync(entity, cancellationToken);
            return entity.Id;
        }
        catch (DbUpdateException)
        {
            var existing = (await _monthlyReportRepository.ListByHouseholdAsync(householdId, year, month, cancellationToken))
                .FirstOrDefault();
            return existing?.Id;
        }
    }

    public async Task<MonthlyReportListItemDto?> RegenerateReportAsync(
        Guid reportId,
        Guid householdId,
        Guid actingUserId,
        CancellationToken cancellationToken = default)
    {
        var report = await _monthlyReportRepository.GetByIdAsync(reportId, cancellationToken);
        if (report == null || report.HouseholdId != householdId)
            return null;

        if (!await _subscriptionService.CanAccessMonthlyReportsAsync(householdId, cancellationToken))
            return null;

        var dashboard = await _dashboardService.GetDashboardAsync(
            householdId,
            actingUserId,
            report.Year,
            report.Month,
            trendMonths: 120,
            cancellationToken);

        dashboard = CapDashboardToMonth(dashboard, report.Year, report.Month);

        var html = BuildHtmlDocument(dashboard, report.Year, report.Month);
        var pdfBytes = await RenderPdfAsync(html, cancellationToken);
        await _fileStorage.UploadAsync(report.FileRelativePath, pdfBytes, "application/pdf", cancellationToken);

        var generatedAt = DateTime.UtcNow;
        var ok = await _monthlyReportRepository.UpdateGeneratedMetadataAsync(
            reportId,
            generatedAt,
            pdfBytes.LongLength,
            cancellationToken);
        if (!ok)
            return null;

        return new MonthlyReportListItemDto
        {
            Id = reportId,
            Year = report.Year,
            Month = report.Month,
            GeneratedAt = generatedAt,
            FileSizeBytes = pdfBytes.LongLength
        };
    }

    private static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
            return TimeZoneInfo.Utc;
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
        }
        catch
        {
            return TimeZoneInfo.Utc;
        }
    }

    private static async Task<byte[]> RenderPdfAsync(string html, CancellationToken cancellationToken)
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        var page = await browser.NewPageAsync();
        await page.SetContentAsync(html, new PageSetContentOptions
        {
            WaitUntil = WaitUntilState.Load,
            Timeout = 120_000
        });
        // Allow CDN + Chart.js to load and paint
        await Task.Delay(2500, cancellationToken);
        var bytes = await page.PdfAsync(new PagePdfOptions
        {
            Format = "A4",
            PrintBackground = true,
            Margin = new Margin { Top = "12mm", Bottom = "12mm", Left = "12mm", Right = "12mm" }
        });
        return bytes;
    }

    /// <summary>
    /// Filters dashboard data so that only information up to the given month is included.
    /// Trend data after the report month is removed.
    /// </summary>
    private static DashboardDto CapDashboardToMonth(DashboardDto d, int year, int month)
    {
        var capYm = year * 12 + month;
        var filteredTrend = d.MonthlyTrend
            .Where(t => t.Year * 12 + t.Month <= capYm)
            .ToList();

        return d with { MonthlyTrend = filteredTrend };
    }

    private static string BuildHtmlDocument(DashboardDto d, int year, int month)
    {
        var culture = CultureInfo.GetCultureInfo("pt-PT");
        var monthTitle = culture.DateTimeFormat.GetMonthName(month);
        monthTitle = char.ToUpper(monthTitle[0], culture) + monthTitle.Substring(1);

        var expenseLabels = d.ExpensesByCategory.Select(x => x.CategoryName).ToList();
        var expenseData = d.ExpensesByCategory.Select(x => (double)x.Amount).ToList();
        var incomeLabels = d.IncomeByCategory.Select(x => x.CategoryName).ToList();
        var incomeData = d.IncomeByCategory.Select(x => (double)x.Amount).ToList();
        var trendLabels = d.MonthlyTrend.Select(x => x.Label).ToList();
        var trendIncome = d.MonthlyTrend.Select(x => (double)x.Income).ToList();
        var trendExpenses = d.MonthlyTrend.Select(x => (double)x.Expenses).ToList();
        var trendYears = d.MonthlyTrend.Select(x => x.Year).ToList();
        var trendMonthNums = d.MonthlyTrend.Select(x => x.Month).ToList();

        const string arrowUp = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2.5\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><polyline points=\"17 11 12 6 7 11\"/><line x1=\"12\" x2=\"12\" y1=\"6\" y2=\"18\"/></svg>";
        const string arrowDown = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2.5\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><polyline points=\"7 13 12 18 17 13\"/><line x1=\"12\" x2=\"12\" y1=\"18\" y2=\"6\"/></svg>";
        const string piggy = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><path d=\"M19 5c-1.5 0-2.8 1.4-3 2-3.5-1.5-11-.3-11 5 0 1.8 0 3 2 4.5V20h4v-2h3v2h4v-4c1-.5 1.7-1 2-2h2v-4h-2c0-1-.5-1.5-1-2\"/><path d=\"M2 9.1C1.7 11 2 12 2 12\"/><circle cx=\"15.5\" cy=\"9.5\" r=\".5\" fill=\"currentColor\"/></svg>";

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\"/>");
        sb.AppendLine("<style>");
        sb.AppendLine("*{box-sizing:border-box;}");
        sb.AppendLine("body{font-family:'Segoe UI',system-ui,-apple-system,sans-serif;margin:0;padding:20px;color:#1a1a1a;background:#f8fafc;}");
        sb.AppendLine("h1{font-size:22px;margin:0 0 2px;font-weight:700;}");
        sb.AppendLine(".sub{color:#64748b;font-size:12px;margin-bottom:20px;}");
        // KPI cards
        sb.AppendLine(".kpis{display:flex;gap:12px;margin-bottom:22px;}");
        sb.AppendLine(".kpi{background:#fff;border-radius:10px;padding:14px 18px;flex:1;box-shadow:0 1px 3px rgba(0,0,0,.06);border:1px solid #f1f5f9;}");
        sb.AppendLine(".kpi-header{display:flex;align-items:center;gap:6px;margin-bottom:6px;}");
        sb.AppendLine(".kpi-label{font-size:11px;text-transform:uppercase;color:#94a3b8;font-weight:600;letter-spacing:.3px;}");
        sb.AppendLine(".kpi-icon{display:flex;align-items:center;}");
        sb.AppendLine(".kpi-icon--income{color:#16a34a;}");
        sb.AppendLine(".kpi-icon--expense{color:#dc2626;}");
        sb.AppendLine(".kpi-icon--savings{color:#ca8a04;}");
        sb.AppendLine(".kpi-val{font-size:17px;font-weight:700;white-space:nowrap;}");
        // Panels
        sb.AppendLine(".panel{background:#fff;border-radius:10px;padding:16px;box-shadow:0 1px 3px rgba(0,0,0,.06);border:1px solid #f1f5f9;margin-bottom:16px;}");
        sb.AppendLine(".panel h2{font-size:14px;margin:0 0 12px;font-weight:600;color:#334155;}");
        // Category row: chart + table side by side, card grows with table
        sb.AppendLine(".cat-row{display:flex;gap:16px;align-items:flex-start;}");
        sb.AppendLine(".cat-chart{width:220px;min-width:220px;height:220px;position:relative;flex-shrink:0;}");
        sb.AppendLine(".cat-chart canvas{max-width:100%;max-height:100%;}");
        sb.AppendLine(".cat-table{flex:1;min-width:0;}");
        // Stacked variant: chart on top, table below — used when many categories
        sb.AppendLine(".cat-stacked .cat-chart{width:100%;min-width:0;height:260px;max-width:320px;margin:0 auto 12px;}");
        sb.AppendLine(".cat-stacked{display:block;}");
        // Tables
        sb.AppendLine("table{width:100%;border-collapse:collapse;font-size:12px;}");
        sb.AppendLine("th,td{padding:8px 10px;text-align:left;border-bottom:1px solid #f1f5f9;}");
        sb.AppendLine("th{color:#64748b;font-weight:600;font-size:11px;text-transform:uppercase;letter-spacing:.3px;}");
        sb.AppendLine("td{color:#334155;}");
        // Full-width chart
        sb.AppendLine(".chart-full{position:relative;height:280px;}");
        sb.AppendLine(".chart-full canvas{max-width:100%;max-height:100%;}");
        // Sankey — matches dashboard TransactionsView
        sb.AppendLine(".sankey{display:flex;align-items:stretch;position:relative;gap:0;}");
        sb.AppendLine(".sankey-col{position:relative;flex-shrink:0;}");
        sb.AppendLine(".sankey-col-left,.sankey-col-right{width:110px;}");
        sb.AppendLine(".sankey-col-center{display:flex;align-items:center;justify-content:center;width:80px;flex-shrink:0;}");
        sb.AppendLine(".sankey-svg{flex:1;height:100%;min-width:60px;}");
        sb.AppendLine(".sankey-node{position:absolute;left:0;right:0;display:flex;align-items:center;gap:6px;}");
        sb.AppendLine(".sankey-col-left .sankey-node{justify-content:flex-end;}");
        sb.AppendLine(".sankey-col-right .sankey-node{justify-content:flex-start;}");
        sb.AppendLine(".sankey-node-bar{width:5px;height:100%;border-radius:3px;flex-shrink:0;}");
        sb.AppendLine(".sankey-node-label{display:flex;flex-direction:column;gap:1px;overflow:hidden;min-width:0;}");
        sb.AppendLine(".sankey-node-label-left{text-align:right;align-items:flex-end;order:-1;}");
        sb.AppendLine(".sankey-node-label-right{text-align:left;align-items:flex-start;}");
        sb.AppendLine(".sankey-node-name{font-size:11px;font-weight:600;color:#334155;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;line-height:1.2;}");
        sb.AppendLine(".sankey-node-amount{font-size:10px;font-weight:500;color:#94a3b8;white-space:nowrap;line-height:1.2;}");
        sb.AppendLine(".sankey-center-node{display:flex;flex-direction:column;align-items:center;justify-content:center;position:relative;}");
        sb.AppendLine(".sankey-center-bar{position:absolute;left:50%;transform:translateX(-50%);width:5px;height:100%;border-radius:3px;background:linear-gradient(180deg,#059669 0%,#10b981 100%);}");
        sb.AppendLine(".sankey-center-label{position:relative;z-index:1;display:flex;flex-direction:column;align-items:center;background:#fff;padding:4px 6px;border-radius:6px;}");
        sb.AppendLine(".sankey-center-amount{font-size:12px;font-weight:700;color:#334155;white-space:nowrap;}");
        sb.AppendLine(".sankey-center-sub{font-size:9px;color:#94a3b8;}");
        sb.AppendLine("@media print{.panel{break-inside:avoid;}.panel-breakable{break-inside:auto;}.panel-breakable .cat-chart{break-inside:avoid;}.panel-breakable .cat-table{break-inside:auto;}.kpis{break-inside:avoid;}}");
        sb.AppendLine("</style>");
        sb.AppendLine("<script src=\"https://cdn.jsdelivr.net/npm/chart.js@4.4.1/dist/chart.umd.min.js\"></script>");
        sb.AppendLine("</head><body>");

        // Header
        sb.AppendLine($"<h1>Relatório mensal — {monthTitle} {year}</h1>");
        sb.AppendLine("<p class=\"sub\">Resumo gerado automaticamente (Finora)</p>");

        // KPI cards with icons
        sb.AppendLine("<div class=\"kpis\">");
        sb.AppendLine(Kpi("Receitas", d.MonthlyIncome, d.Currency, arrowUp, "income"));
        sb.AppendLine(Kpi("Despesas", d.MonthlyExpenses, d.Currency, arrowDown, "expense"));
        sb.AppendLine(Kpi("Poupança", d.MonthlySavings, d.Currency, piggy, "savings"));
        sb.AppendLine(KpiPlain("Saldo total", d.TotalBalance, d.Currency));
        sb.AppendLine("</div>");

        // ── Receitas: pie chart on top + detail table below ──
        var incomeStacked = d.IncomeByCategory.Count > 6;
        sb.AppendLine(incomeStacked ? "<div class=\"panel panel-breakable\">" : "<div class=\"panel\">");
        sb.AppendLine("<h2>Receitas por categoria</h2>");
        sb.AppendLine("<div class=\"cat-row cat-stacked\">");
        sb.AppendLine("<div class=\"cat-chart\"><canvas id=\"cInc\"></canvas></div>");
        sb.AppendLine("<div class=\"cat-table\">");
        sb.AppendLine("<table><thead><tr><th>Categoria</th><th>Valor</th><th>%</th></tr></thead><tbody>");
        foreach (var row in d.IncomeByCategory)
            sb.AppendLine($"<tr><td>{System.Net.WebUtility.HtmlEncode(row.CategoryName)}</td><td>{row.Amount:N2} {d.Currency}</td><td>{row.Percentage:N1}%</td></tr>");
        sb.AppendLine("</tbody></table></div></div></div>");

        // ── Despesas: pie chart on top + detail table below ──
        var expenseStacked = d.ExpensesByCategory.Count > 6;
        sb.AppendLine(expenseStacked ? "<div class=\"panel panel-breakable\">" : "<div class=\"panel\">");
        sb.AppendLine("<h2>Despesas por categoria</h2>");
        sb.AppendLine("<div class=\"cat-row cat-stacked\">");
        sb.AppendLine("<div class=\"cat-chart\"><canvas id=\"cExp\"></canvas></div>");
        sb.AppendLine("<div class=\"cat-table\">");
        sb.AppendLine("<table><thead><tr><th>Categoria</th><th>Valor</th><th>%</th></tr></thead><tbody>");
        foreach (var row in d.ExpensesByCategory)
            sb.AppendLine($"<tr><td>{System.Net.WebUtility.HtmlEncode(row.CategoryName)}</td><td>{row.Amount:N2} {d.Currency}</td><td>{row.Percentage:N1}%</td></tr>");
        sb.AppendLine("</tbody></table></div></div></div>");

        // ── Sankey: income categories → total → expense categories ──
        sb.AppendLine("<div class=\"panel\">");
        sb.AppendLine("<h2>Fluxo de receitas e despesas</h2>");
        sb.AppendLine("<div id=\"sankeyContainer\" class=\"sankey\"></div>");
        sb.AppendLine("</div>");

        // ── Saldos por conta (full width) ──
        if (d.AccountBalancesAtPeriod.Count > 0)
        {
            sb.AppendLine("<div class=\"panel\">");
            sb.AppendLine("<h2>Saldos por conta (fim do período)</h2>");
            sb.AppendLine("<table><thead><tr><th>Conta</th><th style=\"text-align:right\">Saldo</th></tr></thead><tbody>");
            foreach (var a in d.AccountBalancesAtPeriod)
                sb.AppendLine($"<tr><td>{System.Net.WebUtility.HtmlEncode(a.Name)}</td><td style=\"text-align:right;font-weight:600\">{a.Balance:N2} {a.Currency}</td></tr>");
            sb.AppendLine("</tbody></table></div>");
        }

        // ── Tendência mensal — title inside the card ──
        sb.AppendLine("<div class=\"panel\">");
        sb.AppendLine("<h2>Tendência mensal — Evolução</h2>");
        sb.AppendLine("<div class=\"chart-full\"><canvas id=\"cTrend\"></canvas></div>");
        sb.AppendLine("</div>");

        sb.AppendLine("<div class=\"panel\">");
        sb.AppendLine("<h2>Tendência mensal — Receitas vs Despesas</h2>");
        sb.AppendLine("<div class=\"chart-full\"><canvas id=\"cBar\"></canvas></div>");
        sb.AppendLine("</div>");

        // ── JavaScript ──
        var el = JsonSerializer.Serialize(expenseLabels, JsonHtmlSafe);
        var ed = JsonSerializer.Serialize(expenseData, JsonHtmlSafe);
        var il = JsonSerializer.Serialize(incomeLabels, JsonHtmlSafe);
        var ida = JsonSerializer.Serialize(incomeData, JsonHtmlSafe);
        var tl = JsonSerializer.Serialize(trendLabels, JsonHtmlSafe);
        var ti = JsonSerializer.Serialize(trendIncome, JsonHtmlSafe);
        var te = JsonSerializer.Serialize(trendExpenses, JsonHtmlSafe);

        var ty = JsonSerializer.Serialize(trendYears, JsonHtmlSafe);
        var tm = JsonSerializer.Serialize(trendMonthNums, JsonHtmlSafe);

        sb.AppendLine("<script>");
        sb.AppendLine($"const expenseLabels={el};const expenseData={ed};");
        sb.AppendLine($"const incomeLabels={il};const incomeData={ida};");
        sb.AppendLine($"const rawTrendLabels={tl};const rawTrendIncome={ti};const rawTrendExpenses={te};");
        sb.AppendLine($"const trendYears={ty};const trendMonthNums={tm};");
        // Smart aggregation: if >12 data points, group complete past years into single entries
        sb.AppendLine("function aggregateTrend(labels,income,expenses,years,months){");
        sb.AppendLine("  if(labels.length<=12) return {labels,income,expenses};");
        sb.AppendLine("  const lastYear=years[years.length-1];");
        sb.AppendLine("  const yearSet=[...new Set(years.filter(y=>y<lastYear))];");
        sb.AppendLine("  const aLabels=[],aIncome=[],aExpenses=[];");
        sb.AppendLine("  yearSet.forEach(yr=>{let si=0,se=0;for(let i=0;i<labels.length;i++){if(years[i]===yr){si+=income[i];se+=expenses[i];}}aLabels.push(String(yr));aIncome.push(si);aExpenses.push(se);});");
        sb.AppendLine("  for(let i=0;i<labels.length;i++){if(years[i]===lastYear){aLabels.push(labels[i]);aIncome.push(income[i]);aExpenses.push(expenses[i]);}}");
        sb.AppendLine("  return {labels:aLabels,income:aIncome,expenses:aExpenses};}");
        sb.AppendLine("const _agg=aggregateTrend(rawTrendLabels,rawTrendIncome,rawTrendExpenses,trendYears,trendMonthNums);");
        sb.AppendLine("const trendLabels=_agg.labels;const trendIncome=_agg.income;const trendExpenses=_agg.expenses;");
        sb.AppendLine("const palette=['#2563eb','#16a34a','#dc2626','#ca8a04','#9333ea','#0891b2','#ea580c','#4f46e5'];");

        // Pie chart
        sb.AppendLine("function pieChart(id,labels,data){const ctx=document.getElementById(id);if(!ctx||!labels.length)return;const bg=labels.map((_,i)=>palette[i%palette.length]);new Chart(ctx,{type:'doughnut',data:{labels,datasets:[{data,backgroundColor:bg,borderWidth:2,borderColor:'#fff'}]},options:{responsive:true,maintainAspectRatio:false,cutout:'55%',layout:{padding:4},plugins:{legend:{position:'bottom',labels:{padding:8,font:{size:10},usePointStyle:true,pointStyle:'circle'}}}}});}");

        // Line chart
        sb.AppendLine("function trendChart(){const ctx=document.getElementById('cTrend');if(!ctx)return;new Chart(ctx,{type:'line',data:{labels:trendLabels,datasets:[{label:'Receitas',data:trendIncome,borderColor:'#16a34a',backgroundColor:'rgba(22,163,74,.12)',fill:true,tension:.3,pointRadius:4,pointBackgroundColor:'#16a34a'},{label:'Despesas',data:trendExpenses,borderColor:'#dc2626',backgroundColor:'rgba(220,38,38,.08)',fill:true,tension:.3,pointRadius:4,pointBackgroundColor:'#dc2626'}]},options:{responsive:true,maintainAspectRatio:false,plugins:{legend:{labels:{usePointStyle:true,padding:12,font:{size:11}}}},scales:{x:{grid:{display:false}},y:{beginAtZero:true,grid:{color:'rgba(0,0,0,.04)'}}}}});}");

        // Bar chart
        sb.AppendLine("function barChart(){const ctx=document.getElementById('cBar');if(!ctx)return;new Chart(ctx,{type:'bar',data:{labels:trendLabels,datasets:[{label:'Receitas',data:trendIncome,backgroundColor:'rgba(5,150,105,.75)',borderColor:'#059669',borderWidth:1,borderRadius:4},{label:'Despesas',data:trendExpenses,backgroundColor:'rgba(220,38,38,.75)',borderColor:'#dc2626',borderWidth:1,borderRadius:4}]},options:{responsive:true,maintainAspectRatio:false,interaction:{mode:'index',intersect:false},plugins:{legend:{labels:{usePointStyle:true,padding:12,font:{size:11}}}},scales:{x:{grid:{display:false}},y:{beginAtZero:true,grid:{color:'rgba(0,0,0,.04)'}}}}});}");

        // Sankey builder — matches dashboard TransactionsView exactly
        sb.AppendLine("function buildSankey(){");
        sb.AppendLine("const c=document.getElementById('sankeyContainer');if(!c)return;");
        sb.AppendLine("const totalInc=incomeData.reduce((a,b)=>a+b,0);const totalExp=expenseData.reduce((a,b)=>a+b,0);");
        sb.AppendLine("if(totalInc===0&&totalExp===0)return;");
        // Category colors matching frontend
        sb.AppendLine("const catColors={'Salário':'#059669','Freelance':'#10b981','Investimento':'#0ea5e9','Presente':'#8b5cf6','Reembolso':'#6366f1','Alimentação':'#f97316','Transportes':'#eab308','Habitação':'#ef4444','Utilidades':'#84cc16','Saúde':'#ec4899','Entretenimento':'#a855f7','Compras':'#f43f5e','Educação':'#14b8a6','Outro':'#94a3b8','Transferência':'#2563eb'};");
        sb.AppendLine("function getColor(name){return catColors[name]||palette[0];}");
        // Layout constants
        sb.AppendLine("const nodeGap=18,minH=4,svgW=200;");
        sb.AppendLine("const maxNodes=Math.max(incomeLabels.length,expenseLabels.length,1);");
        sb.AppendLine("const H=Math.max(280,maxNodes*40+40);c.style.height=H+'px';");
        // buildNodes — exactly like dashboard buildSankeyNodes
        sb.AppendLine("function buildNodes(labels,data,scaleTotal){");
        sb.AppendLine("  if(!labels.length)return[];const n=labels.length;const gapsTotal=(n-1)*nodeGap;");
        sb.AppendLine("  const itemsTotal=data.reduce((s,v)=>s+v,0);const ratio=scaleTotal>0?itemsTotal/scaleTotal:1;");
        sb.AppendLine("  const barsH=(H-gapsTotal)*ratio;const baseTotal=n*minH;const extra=Math.max(0,barsH-baseTotal);");
        sb.AppendLine("  const nodes=labels.map((name,i)=>({name,amount:data[i],color:getColor(name),h:minH+(itemsTotal>0?(data[i]/itemsTotal)*extra:0),y:0}));");
        sb.AppendLine("  let yy=0;nodes.forEach(nd=>{nd.y=yy;yy+=nd.h+nodeGap;});");
        sb.AppendLine("  const totalUsed=yy-nodeGap;const offset=(H-totalUsed)/2;nodes.forEach(nd=>{nd.y+=offset;});return nodes;}");
        sb.AppendLine("const incNodes=buildNodes(incomeLabels,incomeData,totalInc);");
        sb.AppendLine("const expNodes=buildNodes(expenseLabels,expenseData,totalExp);");
        // Center height = sum of income bar heights
        sb.AppendLine("const centerH=Math.max(40,incNodes.reduce((s,n)=>s+n.h,0));const centerTop=(H-centerH)/2;");
        // makePath — filled shape with bezier curves (same as dashboard makeSankeyPath)
        sb.AppendLine("function makePath(sy,sH,dy,dH,w){const cx=w*0.5;return 'M0,'+sy+' C'+cx+','+sy+' '+cx+','+dy+' '+w+','+dy+' L'+w+','+(dy+dH)+' C'+cx+','+(dy+dH)+' '+cx+','+(sy+sH)+' 0,'+(sy+sH)+' Z';}");
        sb.AppendLine("function hexRgba(hex,a){const r=parseInt(hex.slice(1,3),16),g=parseInt(hex.slice(3,5),16),b=parseInt(hex.slice(5,7),16);return 'rgba('+r+','+g+','+b+','+a+')';}");
        sb.AppendLine("function fmt(v){return v.toLocaleString('pt-PT',{minimumFractionDigits:2,maximumFractionDigits:2})+' EUR';}");
        // Build income links (income nodes → center)
        sb.AppendLine("const incTotal=incNodes.reduce((s,n)=>s+n.h,0);let incDestY=centerTop;");
        sb.AppendLine("const incLinks=incNodes.map(n=>{const dH=(n.h/incTotal)*centerH;const p=makePath(n.y,n.h,incDestY,dH,svgW);incDestY+=dH;return{path:p,color:hexRgba(n.color,0.35)};});");
        // Build expense links (center → expense nodes)
        sb.AppendLine("const expRatio=totalInc>0?totalExp/totalInc:1;const expCenterH=centerH*Math.min(1,expRatio);");
        sb.AppendLine("const expTotal=expNodes.reduce((s,n)=>s+n.h,0);let expSrcY=centerTop;");
        sb.AppendLine("const expLinks=expNodes.map(n=>{const sH=expTotal>0?(n.h/expTotal)*expCenterH:expCenterH/expNodes.length;const p=makePath(expSrcY,sH,n.y,n.h,svgW);expSrcY+=sH;return{path:p,color:hexRgba(n.color,0.35)};});");
        // Render HTML
        sb.AppendLine("let html='';");
        // Left column: labels on left, bar on right
        sb.AppendLine("html+='<div class=\"sankey-col sankey-col-left\" style=\"height:'+H+'px\">';");
        sb.AppendLine("incNodes.forEach(n=>{html+='<div class=\"sankey-node\" style=\"top:'+n.y+'px;height:'+n.h+'px\">';");
        sb.AppendLine("html+='<span class=\"sankey-node-label sankey-node-label-left\"><span class=\"sankey-node-name\">'+n.name+'</span><span class=\"sankey-node-amount\">'+fmt(n.amount)+'</span></span>';");
        sb.AppendLine("html+='<div class=\"sankey-node-bar\" style=\"background:'+n.color+'\"></div></div>';});");
        sb.AppendLine("html+='</div>';");
        // Left SVG — filled paths
        sb.AppendLine("html+='<svg class=\"sankey-svg\" viewBox=\"0 0 '+svgW+' '+H+'\" preserveAspectRatio=\"none\" style=\"height:'+H+'px\">';");
        sb.AppendLine("incLinks.forEach(l=>{html+='<path d=\"'+l.path+'\" fill=\"'+l.color+'\"/>';});");
        sb.AppendLine("html+='</svg>';");
        // Center column
        sb.AppendLine("html+='<div class=\"sankey-col sankey-col-center\" style=\"height:'+H+'px\">';");
        sb.AppendLine("html+='<div class=\"sankey-center-node\" style=\"height:'+centerH+'px;position:absolute;top:'+centerTop+'px\">';");
        sb.AppendLine("html+='<div class=\"sankey-center-bar\"></div>';");
        sb.AppendLine("html+='<span class=\"sankey-center-label\"><span class=\"sankey-center-amount\">'+fmt(totalInc)+'</span><span class=\"sankey-center-sub\">Total receitas</span></span>';");
        sb.AppendLine("html+='</div></div>';");
        // Right SVG — filled paths
        sb.AppendLine("html+='<svg class=\"sankey-svg\" viewBox=\"0 0 '+svgW+' '+H+'\" preserveAspectRatio=\"none\" style=\"height:'+H+'px\">';");
        sb.AppendLine("expLinks.forEach(l=>{html+='<path d=\"'+l.path+'\" fill=\"'+l.color+'\"/>';});");
        sb.AppendLine("html+='</svg>';");
        // Right column: bar on left, labels on right
        sb.AppendLine("html+='<div class=\"sankey-col sankey-col-right\" style=\"height:'+H+'px\">';");
        sb.AppendLine("expNodes.forEach(n=>{html+='<div class=\"sankey-node\" style=\"top:'+n.y+'px;height:'+n.h+'px\">';");
        sb.AppendLine("html+='<div class=\"sankey-node-bar\" style=\"background:'+n.color+'\"></div>';");
        sb.AppendLine("html+='<span class=\"sankey-node-label sankey-node-label-right\"><span class=\"sankey-node-name\">'+n.name+'</span><span class=\"sankey-node-amount\">'+fmt(n.amount)+'</span></span></div>';});");
        sb.AppendLine("html+='</div>';");
        sb.AppendLine("c.innerHTML=html;}");

        sb.AppendLine("pieChart('cInc',incomeLabels,incomeData);pieChart('cExp',expenseLabels,expenseData);buildSankey();trendChart();barChart();");
        sb.AppendLine("</script></body></html>");

        return sb.ToString();
    }

    private static string Kpi(string label, decimal value, string currency, string icon, string variant)
        => $"<div class=\"kpi\"><div class=\"kpi-header\"><span class=\"kpi-label\">{System.Net.WebUtility.HtmlEncode(label)}</span><span class=\"kpi-icon kpi-icon--{variant}\">{icon}</span></div><div class=\"kpi-val\">{value:N2} {System.Net.WebUtility.HtmlEncode(currency)}</div></div>";

    private static string KpiPlain(string label, decimal value, string currency)
        => $"<div class=\"kpi\"><div class=\"kpi-header\"><span class=\"kpi-label\">{System.Net.WebUtility.HtmlEncode(label)}</span></div><div class=\"kpi-val\">{value:N2} {System.Net.WebUtility.HtmlEncode(currency)}</div></div>";
}
