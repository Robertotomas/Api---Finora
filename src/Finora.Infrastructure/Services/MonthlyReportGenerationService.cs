using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Finora.Application.DTOs.Dashboard;
using Finora.Application.DTOs.Reports;
using Finora.Application.Interfaces;
using Finora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Playwright;

namespace Finora.Infrastructure.Services;

public class MonthlyReportGenerationService : IMonthlyReportGenerationService
{
    private readonly IDashboardService _dashboardService;
    private readonly IMonthlyReportRepository _monthlyReportRepository;
    private readonly IHouseholdRepository _householdRepository;
    private readonly IUserRepository _userRepository;
    private readonly ISubscriptionService _subscriptionService;
    private readonly IHostEnvironment _hostEnvironment;

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
        IHostEnvironment hostEnvironment)
    {
        _dashboardService = dashboardService;
        _monthlyReportRepository = monthlyReportRepository;
        _householdRepository = householdRepository;
        _userRepository = userRepository;
        _subscriptionService = subscriptionService;
        _hostEnvironment = hostEnvironment;
    }

    public async Task GenerateDueReportsAsync(CancellationToken cancellationToken = default)
    {
        var householdIds = await _householdRepository.GetAllHouseholdIdsAsync(cancellationToken);
        foreach (var householdId in householdIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await _subscriptionService.CanAccessMonthlyReportsAsync(householdId, cancellationToken))
                continue;

            var users = await _userRepository.GetByHouseholdIdAsync(householdId, cancellationToken);
            if (users.Count == 0)
                continue;

            var anchorUser = users.OrderBy(u => u.CreatedAt).First();
            var tz = ResolveTimeZone(anchorUser.TimeZoneId);
            var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
            if (localNow.Day != 1)
                continue;

            var reportYear = localNow.Month == 1 ? localNow.Year - 1 : localNow.Year;
            var reportMonth = localNow.Month == 1 ? 12 : localNow.Month - 1;

            if (await _monthlyReportRepository.ExistsAsync(householdId, reportYear, reportMonth, cancellationToken))
                continue;

            await GenerateForHouseholdMonthAsync(householdId, anchorUser.Id, reportYear, reportMonth, cancellationToken);
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
            trendMonths: 6,
            cancellationToken);

        var uploadsRoot = Path.Combine(_hostEnvironment.ContentRootPath, "uploads");
        var householdDir = Path.Combine(uploadsRoot, "reports", householdId.ToString("N"));
        Directory.CreateDirectory(householdDir);

        var fileName = $"{year}-{month:00}.pdf";
        var fullPath = Path.Combine(householdDir, fileName);
        var relativePath = $"reports/{householdId:N}/{fileName}";

        var html = BuildHtmlDocument(dashboard, year, month);
        var pdfBytes = await RenderPdfAsync(html, cancellationToken);

        await File.WriteAllBytesAsync(fullPath, pdfBytes, cancellationToken);

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
            trendMonths: 6,
            cancellationToken);

        var uploadsRoot = Path.Combine(_hostEnvironment.ContentRootPath, "uploads");
        var relativeNorm = report.FileRelativePath.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.Combine(uploadsRoot, relativeNorm);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var html = BuildHtmlDocument(dashboard, report.Year, report.Month);
        var pdfBytes = await RenderPdfAsync(html, cancellationToken);
        await File.WriteAllBytesAsync(fullPath, pdfBytes, cancellationToken);

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

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\"/>");
        sb.AppendLine("<style>");
        sb.AppendLine("body{font-family:Segoe UI,system-ui,sans-serif;margin:0;padding:16px;color:#1a1a1a;background:#fafafa;}");
        sb.AppendLine("h1{font-size:22px;margin:0 0 4px;}");
        sb.AppendLine(".sub{color:#555;font-size:13px;margin-bottom:20px;}");
        sb.AppendLine(".kpis{display:flex;gap:12px;flex-wrap:wrap;margin-bottom:20px;}");
        sb.AppendLine(".kpi{background:#fff;border-radius:8px;padding:12px 16px;min-width:140px;box-shadow:0 1px 3px rgba(0,0,0,.08);}");
        sb.AppendLine(".kpi .label{font-size:11px;text-transform:uppercase;color:#888;}");
        sb.AppendLine(".kpi .val{font-size:20px;font-weight:600;margin-top:4px;}");
        sb.AppendLine(".grid{display:grid;grid-template-columns:1fr 1fr;gap:16px;margin-bottom:16px;}");
        sb.AppendLine(".panel{background:#fff;border-radius:8px;padding:12px;box-shadow:0 1px 3px rgba(0,0,0,.08);}");
        sb.AppendLine(".panel h2{font-size:14px;margin:0 0 8px;}");
        sb.AppendLine("table{width:100%;border-collapse:collapse;font-size:12px;}");
        sb.AppendLine("th,td{padding:6px 8px;text-align:left;border-bottom:1px solid #eee;}");
        sb.AppendLine("th{color:#666;font-weight:600;}");
        sb.AppendLine(".chart-wrap{position:relative;height:220px;}");
        sb.AppendLine("@media print{.kpis{break-inside:avoid;}.panel{break-inside:avoid;}}");
        sb.AppendLine("</style>");
        sb.AppendLine("<script src=\"https://cdn.jsdelivr.net/npm/chart.js@4.4.1/dist/chart.umd.min.js\"></script>");
        sb.AppendLine("</head><body>");

        sb.AppendLine($"<h1>Relatório mensal — {monthTitle} {year}</h1>");
        sb.AppendLine("<p class=\"sub\">Resumo gerado automaticamente (Finora)</p>");

        sb.AppendLine("<div class=\"kpis\">");
        sb.AppendLine(Kpi("Receitas", d.MonthlyIncome, d.Currency));
        sb.AppendLine(Kpi("Despesas", d.MonthlyExpenses, d.Currency));
        sb.AppendLine(Kpi("Poupança", d.MonthlySavings, d.Currency));
        sb.AppendLine(Kpi("Saldo total (contas)", d.TotalBalance, d.Currency));
        sb.AppendLine("</div>");

        sb.AppendLine("<div class=\"grid\">");
        sb.AppendLine("<div class=\"panel\"><h2>Despesas por categoria</h2><div class=\"chart-wrap\"><canvas id=\"cExp\"></canvas></div></div>");
        sb.AppendLine("<div class=\"panel\"><h2>Receitas por categoria</h2><div class=\"chart-wrap\"><canvas id=\"cInc\"></canvas></div></div>");
        sb.AppendLine("</div>");

        sb.AppendLine("<div class=\"panel\" style=\"margin-bottom:16px;\"><h2>Tendência mensal</h2><div class=\"chart-wrap\" style=\"height:260px;\"><canvas id=\"cTrend\"></canvas></div></div>");

        sb.AppendLine("<div class=\"grid\">");
        sb.AppendLine("<div class=\"panel\"><h2>Detalhe — despesas</h2>");
        sb.AppendLine("<table><thead><tr><th>Categoria</th><th>Valor</th><th>%</th></tr></thead><tbody>");
        foreach (var row in d.ExpensesByCategory)
            sb.AppendLine($"<tr><td>{System.Net.WebUtility.HtmlEncode(row.CategoryName)}</td><td>{row.Amount:N2} {d.Currency}</td><td>{row.Percentage:N1}%</td></tr>");
        sb.AppendLine("</tbody></table></div>");

        sb.AppendLine("<div class=\"panel\"><h2>Detalhe — receitas</h2>");
        sb.AppendLine("<table><thead><tr><th>Categoria</th><th>Valor</th><th>%</th></tr></thead><tbody>");
        foreach (var row in d.IncomeByCategory)
            sb.AppendLine($"<tr><td>{System.Net.WebUtility.HtmlEncode(row.CategoryName)}</td><td>{row.Amount:N2} {d.Currency}</td><td>{row.Percentage:N1}%</td></tr>");
        sb.AppendLine("</tbody></table></div>");
        sb.AppendLine("</div>");

        if (d.AccountBalancesAtPeriod.Count > 0)
        {
            sb.AppendLine("<div class=\"panel\"><h2>Saldos por conta (fim do período)</h2>");
            sb.AppendLine("<table><thead><tr><th>Conta</th><th>Saldo</th></tr></thead><tbody>");
            foreach (var a in d.AccountBalancesAtPeriod)
                sb.AppendLine($"<tr><td>{System.Net.WebUtility.HtmlEncode(a.Name)}</td><td>{a.Balance:N2} {a.Currency}</td></tr>");
            sb.AppendLine("</tbody></table></div>");
        }

        var el = JsonSerializer.Serialize(expenseLabels, JsonHtmlSafe);
        var ed = JsonSerializer.Serialize(expenseData, JsonHtmlSafe);
        var il = JsonSerializer.Serialize(incomeLabels, JsonHtmlSafe);
        var ida = JsonSerializer.Serialize(incomeData, JsonHtmlSafe);
        var tl = JsonSerializer.Serialize(trendLabels, JsonHtmlSafe);
        var ti = JsonSerializer.Serialize(trendIncome, JsonHtmlSafe);
        var te = JsonSerializer.Serialize(trendExpenses, JsonHtmlSafe);

        sb.AppendLine("<script>");
        sb.AppendLine($"const expenseLabels = {el}; const expenseData = {ed};");
        sb.AppendLine($"const incomeLabels = {il}; const incomeData = {ida};");
        sb.AppendLine($"const trendLabels = {tl}; const trendIncome = {ti}; const trendExpenses = {te};");
        sb.AppendLine("const palette = ['#2563eb','#16a34a','#dc2626','#ca8a04','#9333ea','#0891b2','#ea580c','#4f46e5'];");
        sb.AppendLine("function pieChart(id, labels, data){ const ctx = document.getElementById(id); if(!ctx) return; const bg = labels.map((_,i)=>palette[i%palette.length]); new Chart(ctx,{ type:'doughnut', data:{ labels, datasets:[{ data, backgroundColor:bg }] }, options:{ plugins:{ legend:{ position:'bottom' } }, maintainAspectRatio:false } }); }");
        sb.AppendLine("function trendChart(){ const ctx = document.getElementById('cTrend'); if(!ctx) return; new Chart(ctx,{ type:'line', data:{ labels: trendLabels, datasets:[{ label:'Receitas', data: trendIncome, borderColor:'#16a34a', backgroundColor:'rgba(22,163,74,.15)', fill:true, tension:.25 },{ label:'Despesas', data: trendExpenses, borderColor:'#dc2626', backgroundColor:'rgba(220,38,38,.1)', fill:true, tension:.25 }] }, options:{ maintainAspectRatio:false, scales:{ y:{ beginAtZero:true } } } }); }");
        sb.AppendLine("pieChart('cExp', expenseLabels, expenseData); pieChart('cInc', incomeLabels, incomeData); trendChart();");
        sb.AppendLine("</script></body></html>");

        return sb.ToString();
    }

    private static string Kpi(string label, decimal value, string currency)
        => $"<div class=\"kpi\"><div class=\"label\">{System.Net.WebUtility.HtmlEncode(label)}</div><div class=\"val\">{value:N2} {System.Net.WebUtility.HtmlEncode(currency)}</div></div>";
}
