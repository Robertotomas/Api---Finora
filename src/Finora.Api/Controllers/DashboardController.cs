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

            // 2. Get ALL real transactions (not just in range) to compute initial balances per account
            var allTransactions = await _dashboardRepository.GetTransactionsWithAccountInRangeAsync(
                householdId.Value, DateTime.MinValue, today.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), cancellationToken);

            // 3. Compute initial balance per account: Balance - sum of all transaction effects
            //    Also find the earliest transaction date per account
            //    For transfers: source account loses Amount, destination gains Amount
            var txSumByAccount = new Dictionary<Guid, decimal>();
            var earliestTxByAccount = new Dictionary<Guid, DateTime>();
            foreach (var tx in allTransactions)
            {
                if (tx.Type == TransactionType.Transfer)
                {
                    // Source loses money
                    txSumByAccount[tx.AccountId] = txSumByAccount.GetValueOrDefault(tx.AccountId) - tx.Amount;
                    // Destination gains money
                    if (tx.DestinationAccountId.HasValue)
                        txSumByAccount[tx.DestinationAccountId.Value] = txSumByAccount.GetValueOrDefault(tx.DestinationAccountId.Value) + tx.Amount;
                }
                else
                {
                    var delta = tx.Type == TransactionType.Income ? tx.Amount : -tx.Amount;
                    txSumByAccount[tx.AccountId] = txSumByAccount.GetValueOrDefault(tx.AccountId) + delta;
                }

                // Track earliest tx date per account (source)
                if (!earliestTxByAccount.TryGetValue(tx.AccountId, out var earliest) || tx.Date < earliest)
                    earliestTxByAccount[tx.AccountId] = tx.Date;
                // Track earliest tx date for destination account too
                if (tx.DestinationAccountId.HasValue)
                {
                    if (!earliestTxByAccount.TryGetValue(tx.DestinationAccountId.Value, out var destEarliest) || tx.Date < destEarliest)
                        earliestTxByAccount[tx.DestinationAccountId.Value] = tx.Date;
                }
            }

            var accountInitialBalances = activeAccounts.Select(a =>
            {
                var createdDate = DateOnly.FromDateTime(a.CreatedAt);
                // Use earliest of: account creation date or first transaction date
                if (earliestTxByAccount.TryGetValue(a.Id, out var firstTx))
                {
                    var firstTxDate = DateOnly.FromDateTime(firstTx);
                    if (firstTxDate < createdDate)
                        createdDate = firstTxDate;
                }
                return new
                {
                    a.Id,
                    InitialBalance = a.Balance - txSumByAccount.GetValueOrDefault(a.Id, 0m),
                    CreatedDate = createdDate
                };
            }).ToList();

            // 4. Get transactions in chart range for daily deltas
            var fromDt = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var tomorrowDt = today.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

            var rangeTransactions = allTransactions
                .Where(t => t.Date >= fromDt && t.Date < tomorrowDt)
                .ToList();

            // 5. Get recurring transactions
            var recurringTxs = await _recurringRepository.GetByHouseholdAsync(householdId.Value, cancellationToken);
            var activeRecurring = recurringTxs.ToList();
            var archivedIds = accounts.Where(a => a.IsArchived).Select(a => a.Id).ToHashSet();

            // 6. Build cumulative transaction effects per account up to each day (forward approach)
            // Group all range transactions by (date, accountId), handling transfers
            var txByDateAccount = new Dictionary<(DateOnly, Guid), decimal>();
            foreach (var tx in rangeTransactions)
            {
                var d = DateOnly.FromDateTime(tx.Date);
                if (tx.Type == TransactionType.Transfer)
                {
                    var srcKey = (d, tx.AccountId);
                    txByDateAccount[srcKey] = txByDateAccount.GetValueOrDefault(srcKey) - tx.Amount;
                    if (tx.DestinationAccountId.HasValue)
                    {
                        var dstKey = (d, tx.DestinationAccountId.Value);
                        txByDateAccount[dstKey] = txByDateAccount.GetValueOrDefault(dstKey) + tx.Amount;
                    }
                }
                else
                {
                    var delta = tx.Type == TransactionType.Income ? tx.Amount : -tx.Amount;
                    var key = (d, tx.AccountId);
                    txByDateAccount[key] = txByDateAccount.GetValueOrDefault(key) + delta;
                }
            }

            // 7. For transactions BEFORE the chart range, compute their cumulative effect per account
            var priorTxByAccount = new Dictionary<Guid, decimal>();
            foreach (var tx in allTransactions)
            {
                if (tx.Date >= fromDt) continue; // already in range
                if (tx.Type == TransactionType.Transfer)
                {
                    priorTxByAccount[tx.AccountId] = priorTxByAccount.GetValueOrDefault(tx.AccountId) - tx.Amount;
                    if (tx.DestinationAccountId.HasValue)
                        priorTxByAccount[tx.DestinationAccountId.Value] = priorTxByAccount.GetValueOrDefault(tx.DestinationAccountId.Value) + tx.Amount;
                }
                else
                {
                    var delta = tx.Type == TransactionType.Income ? tx.Amount : -tx.Amount;
                    priorTxByAccount[tx.AccountId] = priorTxByAccount.GetValueOrDefault(tx.AccountId) + delta;
                }
            }

            // 8. Build daily balance going forward
            // For each day d, balance = sum over active accounts of:
            //   if account existed on d: initialBalance + priorTxs + cumulativeTxs up to d
            //   else: 0
            // Plus recurring adjustment (only for accounts that existed on that day)
            var accountCreationDates = accountInitialBalances.ToDictionary(a => a.Id, a => a.CreatedDate);

            var points = new List<object>();
            // Running cumulative tx per account (within chart range)
            var runningTxByAccount = new Dictionary<Guid, decimal>();

            for (var d = from; d <= today; d = d.AddDays(1))
            {
                // Add this day's transactions to running totals
                foreach (var acc in accountInitialBalances)
                {
                    var key = (d, acc.Id);
                    if (txByDateAccount.TryGetValue(key, out var dayDelta))
                        runningTxByAccount[acc.Id] = runningTxByAccount.GetValueOrDefault(acc.Id) + dayDelta;
                }

                // Calculate balance: sum of each account's contribution
                var balance = 0m;
                foreach (var acc in accountInitialBalances)
                {
                    if (d < acc.CreatedDate) continue; // account didn't exist yet
                    balance += acc.InitialBalance
                             + priorTxByAccount.GetValueOrDefault(acc.Id)
                             + runningTxByAccount.GetValueOrDefault(acc.Id);
                }

                // Add recurring adjustment ONLY for the current month (projection)
                // Past months use only real transaction data — recurring effects either
                // already became real transactions or didn't happen
                if ((d.Year, d.Month) == (today.Year, today.Month))
                {
                    var existingAccounts = activeAccounts
                        .Where(a => accountCreationDates.TryGetValue(a.Id, out var created) &&
                                    (created.Year < d.Year || (created.Year == d.Year && created.Month <= d.Month)))
                        .ToList();
                    var recurringAdj = CalculateRecurringAdjustment(
                        activeRecurring, existingAccounts, archivedIds, d.Year, d.Month);
                    balance += recurringAdj;
                }

                points.Add(new { date = d.ToString("yyyy-MM-dd"), balance = Math.Round(balance, 2) });
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

                var amount = r.AmountForMonth(m);
                if (amount == 0m) continue;

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
        => r.IsActiveInMonth(y, m);
}
