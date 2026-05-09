using System.Security.Claims;
using Finora.Application.DTOs.Dashboard;
using Finora.Application.Interfaces;
using Finora.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Finora.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly IDashboardService _dashboardService;
    private readonly IHouseholdService _householdService;
    private readonly IDashboardRepository _dashboardRepository;
    private readonly IRecurringTransactionRepository _recurringRepository;
    private readonly IAccountRepository _accountRepository;

    public DashboardController(
        IDashboardService dashboardService,
        IHouseholdService householdService,
        IDashboardRepository dashboardRepository,
        IRecurringTransactionRepository recurringRepository,
        IAccountRepository accountRepository)
    {
        _dashboardService = dashboardService;
        _householdService = householdService;
        _dashboardRepository = dashboardRepository;
        _recurringRepository = recurringRepository;
        _accountRepository = accountRepository;
    }

    private Guid? UserId
    {
        get
        {
            var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(id, out var guid) ? guid : null;
        }
    }

    private Guid? HouseholdIdFromClaim
    {
        get
        {
            var id = User.FindFirstValue("household_id");
            return !string.IsNullOrEmpty(id) && Guid.TryParse(id, out var guid) ? guid : null;
        }
    }

    private async Task<Guid?> ResolveHouseholdIdAsync(CancellationToken cancellationToken)
    {
        if (HouseholdIdFromClaim is { } id)
            return id;
        if (UserId is not { } userId)
            return null;
        var household = await _householdService.GetOrCreateForUserAsync(userId, cancellationToken);
        return household?.Id;
    }

    /// <summary>
    /// Get dashboard data: total balance, monthly income/expenses, expenses by category, and monthly trend for charts.
    /// </summary>
    /// <param name="year">Optional year (default: current).</param>
    /// <param name="month">Optional month: 1–12 = that month, 0 = full calendar year, -1 = year-to-date (Jan through current month for this year, or full past years).</param>
    /// <param name="trendMonths">Number of months for trend chart (default: 6).</param>
    [HttpGet]
    [ProducesResponseType(typeof(DashboardDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Get(
        [FromQuery] int? year,
        [FromQuery] int? month,
        [FromQuery] int? trendMonths,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (UserId == null)
                return NotFound();

            var householdId = await ResolveHouseholdIdAsync(cancellationToken);
            if (householdId == null)
                return NotFound();

            var months = trendMonths.HasValue ? Math.Clamp(trendMonths.Value, 1, 24) : 6;

            var dashboard = await _dashboardService.GetDashboardAsync(
                householdId.Value,
                UserId!.Value,
                year,
                month,
                months,
                cancellationToken);

            return Ok(dashboard);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message, type = ex.GetType().Name });
        }
    }

    /// <summary>
    /// Returns daily balance snapshots for the patrimônio chart.
    /// Each point is the real total balance (accounts + recurring adjustments) at end of that day.
    /// </summary>
    [HttpGet("daily-balance")]
    public async Task<IActionResult> GetDailyBalance(
        [FromQuery] int days = 180,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (UserId == null)
                return NotFound();

            var householdId = await ResolveHouseholdIdAsync(cancellationToken);
            if (householdId == null)
                return NotFound();

            days = Math.Clamp(days, 7, 1825); // 7 days to 5 years

            var now = DateTime.UtcNow;
            var today = DateOnly.FromDateTime(now);
            var from = today.AddDays(-(days - 1));

            // 1. Get all non-archived accounts with current balance
            var accounts = await _accountRepository.GetByHouseholdIdAsync(householdId.Value, cancellationToken);
            var activeAccounts = accounts.Where(a => !a.IsArchived).ToList();

            // 2. Get all real transactions from 'from' date onwards (we need future deltas to subtract)
            var fromDt = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var tomorrowDt = today.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

            var transactions = await _dashboardRepository.GetTransactionsInRangeAsync(
                householdId.Value, fromDt, tomorrowDt, cancellationToken);

            // 3. Get recurring transactions
            var recurringTxs = await _recurringRepository.GetByHouseholdAsync(householdId.Value, cancellationToken);
            var activeRecurring = recurringTxs.ToList();
            var archivedIds = accounts.Where(a => a.IsArchived).Select(a => a.Id).ToHashSet();

            // 4. Current balance = sum of all account balances + recurring adjustment for current month
            var currentRawBalance = activeAccounts.Sum(a => a.Balance);
            var currentRecurringAdj = CalculateRecurringAdjustment(
                activeRecurring, activeAccounts, archivedIds, today.Year, today.Month);
            var currentBalance = currentRawBalance + currentRecurringAdj;

            // 5. Build cumulative delta from today backwards
            // For each day, we accumulate what changed AFTER that day to subtract from currentBalance
            // Group transactions by date
            var deltaByDate = new Dictionary<DateOnly, decimal>();
            foreach (var tx in transactions)
            {
                var d = DateOnly.FromDateTime(tx.Date);
                var amount = tx.Type == TransactionType.Income ? tx.Amount
                           : tx.Type == TransactionType.Expense ? -tx.Amount
                           : 0m; // transfers are net-zero for total patrimônio
                if (deltaByDate.ContainsKey(d))
                    deltaByDate[d] += amount;
                else
                    deltaByDate[d] = amount;
            }

            // 6. Build daily balance array
            var points = new List<object>();
            var cumulativeDeltaAfter = 0m; // accumulates deltas from days AFTER current iteration day
            var prevMonthKey = (today.Year, today.Month);

            for (var d = today; d >= from; d = d.AddDays(-1))
            {
                var monthKey = (d.Year, d.Month);

                // Calculate recurring difference if we crossed into a different month
                decimal recurringAdj;
                if (monthKey == (today.Year, today.Month))
                {
                    recurringAdj = currentRecurringAdj;
                }
                else
                {
                    recurringAdj = CalculateRecurringAdjustment(
                        activeRecurring, activeAccounts, archivedIds, d.Year, d.Month);
                }

                var balance = currentRawBalance + recurringAdj - cumulativeDeltaAfter;

                points.Add(new { date = d.ToString("yyyy-MM-dd"), balance = Math.Round(balance, 2) });

                // Add this day's delta to cumulative (for the next earlier day)
                if (deltaByDate.TryGetValue(d, out var dayDelta))
                    cumulativeDeltaAfter += dayDelta;
            }

            points.Reverse(); // chronological order

            // Days before the first-ever transaction: set balance to 0
            // (the app had no data before that — the user hadn't started tracking yet)
            var earliestTx = await _dashboardRepository.GetEarliestTransactionDateAsync(
                householdId.Value, cancellationToken);
            if (earliestTx.HasValue)
            {
                var earliestDate = DateOnly.FromDateTime(earliestTx.Value);
                if (earliestDate > from)
                {
                    points = points
                        .Cast<dynamic>()
                        .Select(p => DateOnly.Parse((string)p.date) < earliestDate
                            ? (object)new { date = (string)p.date, balance = 0m }
                            : (object)p)
                        .ToList();
                }
            }

            return Ok(new { points, currency = activeAccounts.FirstOrDefault()?.Currency ?? "EUR" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message, type = ex.GetType().Name });
        }
    }

    /// <summary>
    /// Calculates total recurring adjustment for all active accounts through the given month.
    /// This mirrors RecurringAccountBalanceService but returns a single total instead of per-account.
    /// </summary>
    private static decimal CalculateRecurringAdjustment(
        List<Domain.Entities.RecurringTransaction> recurring,
        List<Domain.Entities.Account> activeAccounts,
        HashSet<Guid> archivedIds,
        int throughYear,
        int throughMonth)
    {
        if (recurring.Count == 0) return 0m;

        var now = DateTime.UtcNow;
        if (throughYear * 12 + throughMonth > now.Year * 12 + now.Month)
        {
            throughYear = now.Year;
            throughMonth = now.Month;
        }

        var total = 0m;
        var activeAccountIds = activeAccounts.Select(a => a.Id).ToHashSet();

        foreach (var r in recurring)
        {
            // Skip if both accounts are archived or not in active list
            var sourceActive = activeAccountIds.Contains(r.AccountId) && !archivedIds.Contains(r.AccountId);
            var destActive = r.DestinationAccountId.HasValue &&
                           activeAccountIds.Contains(r.DestinationAccountId.Value) &&
                           !archivedIds.Contains(r.DestinationAccountId.Value);

            if (!sourceActive && !destActive) continue;

            var startYm = r.StartYear * 12 + r.StartMonth;
            var endYm = throughYear * 12 + throughMonth;

            for (var ym = startYm; ym <= endYm; ym++)
            {
                var y = (ym - 1) / 12;
                var m = (ym - 1) % 12 + 1;

                if (!IsRecurringActiveInMonth(r, y, m)) continue;

                var amount = r.Frequency == RecurringFrequency.Annual
                    ? Math.Round(r.Amount / 12m, 2)
                    : r.Amount;

                if (r.Type == TransactionType.Transfer)
                {
                    // Transfers: net-zero for total patrimônio (money moves between accounts)
                    // Only affects total if one account is archived
                    if (sourceActive && !destActive)
                        total -= amount;
                    else if (!sourceActive && destActive)
                        total += amount;
                    // else both active: net zero
                }
                else
                {
                    if (sourceActive)
                        total += r.Type == TransactionType.Income ? amount : -amount;
                }
            }
        }

        return total;
    }

    private static bool IsRecurringActiveInMonth(Domain.Entities.RecurringTransaction r, int y, int m)
    {
        var started = r.StartYear < y || (r.StartYear == y && r.StartMonth <= m);
        var notEnded = r.EndYear == null || r.EndYear > y || (r.EndYear == y && (r.EndMonth ?? 13) > m);
        return started && notEnded;
    }
}
