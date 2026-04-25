using Finora.Application.DTOs.Dashboard;
using Finora.Application.Interfaces;
using Finora.Domain.Enums;

namespace Finora.Infrastructure.Services;

public class DashboardService : IDashboardService
{
    private readonly IDashboardRepository _dashboardRepository;
    private readonly IRecurringTransactionService _recurringService;
    private readonly IUserRepository _userRepository;
    private readonly IRecurringAccountBalanceService _recurringAccountBalanceService;

    public DashboardService(
        IDashboardRepository dashboardRepository,
        IRecurringTransactionService recurringService,
        IUserRepository userRepository,
        IRecurringAccountBalanceService recurringAccountBalanceService)
    {
        _dashboardRepository = dashboardRepository;
        _recurringService = recurringService;
        _userRepository = userRepository;
        _recurringAccountBalanceService = recurringAccountBalanceService;
    }

    public async Task<DashboardDto> GetDashboardAsync(Guid householdId, Guid userId, int? year, int? month, int trendMonths = 6, CancellationToken cancellationToken = default)
    {
        if (!await UserBelongsToHouseholdAsync(userId, householdId, cancellationToken))
            return CreateEmptyDashboard(year, month);

        var now = DateTime.UtcNow;
        // year=0 means "all time" - must be handled before any DateTime(year, ...) which throws for year 0
        var useAllTime = year is 0;
        var targetYear = useAllTime ? now.Year : (year ?? now.Year);
        var useYearToDate = month.HasValue && month.Value == -1;
        var useFullYear = !useAllTime && !useYearToDate && (!month.HasValue || month.Value == 0);

        decimal monthlyIncome;
        decimal monthlyExpenses;
        IReadOnlyList<AccountBalanceAtDate> accountBalancesAtPeriod;
        IReadOnlyList<(int Category, decimal Amount)> mergedCategories;
        IReadOnlyList<(int Category, decimal Amount)> mergedIncomeCategories;

        if (useAllTime)
        {
            accountBalancesAtPeriod = await _dashboardRepository.GetAccountBalancesNowAsync(householdId, cancellationToken);
            monthlyIncome = await _dashboardRepository.GetTotalIncomeAsync(householdId, cancellationToken);
            monthlyExpenses = await _dashboardRepository.GetTotalExpensesAsync(householdId, cancellationToken);
            mergedCategories = await _dashboardRepository.GetTotalExpensesByCategoryAsync(householdId, cancellationToken);
            mergedIncomeCategories = await _dashboardRepository.GetTotalIncomeByCategoryAsync(householdId, cancellationToken);

            var earliestTx = await _dashboardRepository.GetEarliestTransactionDateAsync(householdId, cancellationToken);
            var minRec = await _recurringService.GetMinimumRecurringStartMonthAsync(householdId, userId, cancellationToken);
            (int sy, int sm)? rangeStart = null;
            if (earliestTx.HasValue)
                rangeStart = (earliestTx.Value.Year, earliestTx.Value.Month);
            if (minRec.HasValue)
            {
                var (ry, rm) = minRec.Value;
                if (rangeStart == null || ry * 12 + rm < rangeStart.Value.sy * 12 + rangeStart.Value.sm)
                    rangeStart = (ry, rm);
            }

            if (rangeStart.HasValue)
            {
                var recurringAgg = await _recurringService.GetAggregatedForMonthRangeAsync(
                    householdId, userId, rangeStart.Value.sy, rangeStart.Value.sm, now.Year, now.Month, cancellationToken);
                monthlyIncome += recurringAgg.TotalIncome;
                monthlyExpenses += recurringAgg.TotalExpenses;
                mergedCategories = MergeExpensesByCategory(mergedCategories, recurringAgg.ExpensesByCategory);
                mergedIncomeCategories = MergeExpensesByCategory(mergedIncomeCategories, recurringAgg.IncomeByCategory);
            }
        }
        else if (useYearToDate)
        {
            var lastMonthInYear = targetYear > now.Year ? 0 : targetYear < now.Year ? 12 : now.Month;
            if (lastMonthInYear == 0)
            {
                accountBalancesAtPeriod = await _dashboardRepository.GetAccountBalancesAtEndOfMonthAsync(householdId, targetYear, 1, cancellationToken);
                monthlyIncome = 0m;
                monthlyExpenses = 0m;
                mergedCategories = Array.Empty<(int, decimal)>();
                mergedIncomeCategories = Array.Empty<(int, decimal)>();
            }
            else
            {
                accountBalancesAtPeriod = await _dashboardRepository.GetAccountBalancesAtEndOfMonthAsync(householdId, targetYear, lastMonthInYear, cancellationToken);
                monthlyIncome = 0m;
                monthlyExpenses = 0m;
                for (var m = 1; m <= lastMonthInYear; m++)
                {
                    monthlyIncome += await _dashboardRepository.GetMonthlyIncomeAsync(householdId, targetYear, m, cancellationToken);
                    monthlyExpenses += await _dashboardRepository.GetMonthlyExpensesAsync(householdId, targetYear, m, cancellationToken);
                    var (ri, re) = await _recurringService.GetAmountsForMonthAsync(householdId, userId, targetYear, m, cancellationToken);
                    monthlyIncome += ri;
                    monthlyExpenses += re;
                }

                var expensesByCategoryDict = new Dictionary<int, decimal>();
                var incomeByCategoryDict = new Dictionary<int, decimal>();
                for (var m = 1; m <= lastMonthInYear; m++)
                {
                    var expensesByCategory = await _dashboardRepository.GetExpensesByCategoryAsync(householdId, targetYear, m, cancellationToken);
                    foreach (var (cat, amt) in expensesByCategory)
                        expensesByCategoryDict[cat] = expensesByCategoryDict.GetValueOrDefault(cat) + amt;
                    var rc = await _recurringService.GetRecurringExpensesByCategoryAsync(householdId, userId, targetYear, m, cancellationToken);
                    foreach (var (cat, amt) in rc)
                        expensesByCategoryDict[cat] = expensesByCategoryDict.GetValueOrDefault(cat) + amt;

                    var incomeByCategory = await _dashboardRepository.GetIncomeByCategoryAsync(householdId, targetYear, m, cancellationToken);
                    foreach (var (cat, amt) in incomeByCategory)
                        incomeByCategoryDict[cat] = incomeByCategoryDict.GetValueOrDefault(cat) + amt;
                    var ric = await _recurringService.GetRecurringIncomeByCategoryAsync(householdId, userId, targetYear, m, cancellationToken);
                    foreach (var (cat, amt) in ric)
                        incomeByCategoryDict[cat] = incomeByCategoryDict.GetValueOrDefault(cat) + amt;
                }

                mergedCategories = expensesByCategoryDict.OrderByDescending(x => x.Value).Select(x => (x.Key, x.Value)).ToList();
                mergedIncomeCategories = incomeByCategoryDict.OrderByDescending(x => x.Value).Select(x => (x.Key, x.Value)).ToList();
            }
        }
        else if (useFullYear)
        {
            accountBalancesAtPeriod = await _dashboardRepository.GetAccountBalancesAtEndOfYearAsync(householdId, targetYear, cancellationToken);
            monthlyIncome = await _dashboardRepository.GetYearlyIncomeAsync(householdId, targetYear, cancellationToken);
            monthlyExpenses = await _dashboardRepository.GetYearlyExpensesAsync(householdId, targetYear, cancellationToken);

            var recurringTotal = 0m;
            var recurringExpTotal = 0m;
            for (var m = 1; m <= 12; m++)
            {
                var (ri, re) = await _recurringService.GetAmountsForMonthAsync(householdId, userId, targetYear, m, cancellationToken);
                recurringTotal += ri;
                recurringExpTotal += re;
            }
            monthlyIncome += recurringTotal;
            monthlyExpenses += recurringExpTotal;

            var expensesByCategory = await _dashboardRepository.GetYearlyExpensesByCategoryAsync(householdId, targetYear, cancellationToken);
            var recurringByCategoryDict = new Dictionary<int, decimal>();
            for (var m = 1; m <= 12; m++)
            {
                var rc = await _recurringService.GetRecurringExpensesByCategoryAsync(householdId, userId, targetYear, m, cancellationToken);
                foreach (var (cat, amt) in rc)
                    recurringByCategoryDict[cat] = recurringByCategoryDict.GetValueOrDefault(cat) + amt;
            }
            var recurringByCategory = recurringByCategoryDict.Select(x => (x.Key, x.Value)).ToList();
            mergedCategories = MergeExpensesByCategory(expensesByCategory, recurringByCategory);

            var incomeByCategory = await _dashboardRepository.GetYearlyIncomeByCategoryAsync(householdId, targetYear, cancellationToken);
            var recurringIncomeByCategoryDict = new Dictionary<int, decimal>();
            for (var m = 1; m <= 12; m++)
            {
                var ric = await _recurringService.GetRecurringIncomeByCategoryAsync(householdId, userId, targetYear, m, cancellationToken);
                foreach (var (cat, amt) in ric)
                    recurringIncomeByCategoryDict[cat] = recurringIncomeByCategoryDict.GetValueOrDefault(cat) + amt;
            }
            var recurringIncomeByCategory = recurringIncomeByCategoryDict.Select(x => (x.Key, x.Value)).ToList();
            mergedIncomeCategories = MergeExpensesByCategory(incomeByCategory, recurringIncomeByCategory);
        }
        else
        {
            var targetMonth = month!.Value;
            accountBalancesAtPeriod = await _dashboardRepository.GetAccountBalancesAtEndOfMonthAsync(householdId, targetYear, targetMonth, cancellationToken);
            monthlyIncome = await _dashboardRepository.GetMonthlyIncomeAsync(householdId, targetYear, targetMonth, cancellationToken);
            monthlyExpenses = await _dashboardRepository.GetMonthlyExpensesAsync(householdId, targetYear, targetMonth, cancellationToken);

            var (recurringIncome, recurringExpenses) = await _recurringService.GetAmountsForMonthAsync(householdId, userId, targetYear, targetMonth, cancellationToken);
            monthlyIncome += recurringIncome;
            monthlyExpenses += recurringExpenses;

            var expensesByCategory = await _dashboardRepository.GetExpensesByCategoryAsync(householdId, targetYear, targetMonth, cancellationToken);
            var recurringByCategory = await _recurringService.GetRecurringExpensesByCategoryAsync(householdId, userId, targetYear, targetMonth, cancellationToken);
            mergedCategories = MergeExpensesByCategory(expensesByCategory, recurringByCategory);

            var incomeByCategory = await _dashboardRepository.GetIncomeByCategoryAsync(householdId, targetYear, targetMonth, cancellationToken);
            var recurringIncomeByCategory = await _recurringService.GetRecurringIncomeByCategoryAsync(householdId, userId, targetYear, targetMonth, cancellationToken);
            mergedIncomeCategories = MergeExpensesByCategory(incomeByCategory, recurringIncomeByCategory);
        }

        int capYear;
        int capMonth;
        if (useAllTime)
        {
            capYear = now.Year;
            capMonth = now.Month;
        }
        else if (useYearToDate)
        {
            var lastMonthInYear = targetYear > now.Year ? 0 : targetYear < now.Year ? 12 : now.Month;
            if (lastMonthInYear == 0)
            {
                capYear = targetYear;
                capMonth = 1;
            }
            else
            {
                capYear = targetYear;
                capMonth = lastMonthInYear;
            }
        }
        else if (useFullYear)
        {
            capYear = targetYear;
            capMonth = 12;
        }
        else
        {
            capYear = targetYear;
            capMonth = month!.Value;
        }

        accountBalancesAtPeriod = await MergeRecurringIntoAccountBalancesAsync(
            householdId, accountBalancesAtPeriod, capYear, capMonth, cancellationToken);

        var totalBalance = accountBalancesAtPeriod.Sum(a => a.Balance);

        var effectiveTrendMonths = useAllTime ? Math.Max(trendMonths, 60) : trendMonths;
        var monthlyTrend = await _dashboardRepository.GetMonthlyTrendAsync(householdId, effectiveTrendMonths, cancellationToken);

        var startDate = now.AddMonths(-(effectiveTrendMonths - 1));
        var recurringByMonth = await _recurringService.GetAmountsByMonthAsync(
            householdId, userId, startDate.Year, startDate.Month, effectiveTrendMonths, cancellationToken);
        var trendWithRecurring = MergeTrendWithRecurring(monthlyTrend, recurringByMonth);
        var trendFiltered = trendWithRecurring.Where(x => x.Income > 0 || x.Expenses > 0).ToList();

        var categoryDtos = BuildExpensesByCategory(mergedCategories, monthlyExpenses);
        var incomeCategoryDtos = BuildIncomeByCategory(mergedIncomeCategories, monthlyIncome);
        var trendDtos = BuildMonthlyTrend(trendFiltered);

        return new DashboardDto
        {
            TotalBalance = totalBalance,
            Currency = "EUR",
            Year = useAllTime ? 0 : targetYear,
            Month = useAllTime ? 0 : useYearToDate ? -1 : (useFullYear ? 0 : month!.Value),
            MonthlyIncome = monthlyIncome,
            MonthlyExpenses = monthlyExpenses,
            ExpensesByCategory = categoryDtos,
            IncomeByCategory = incomeCategoryDtos,
            MonthlyTrend = trendDtos,
            AccountBalancesAtPeriod = accountBalancesAtPeriod
        };
    }

    private static IReadOnlyList<(int Category, decimal Amount)> MergeExpensesByCategory(
        IReadOnlyList<(int Category, decimal Amount)> transactions,
        IReadOnlyList<(int Category, decimal Amount)> recurring)
    {
        var dict = new Dictionary<int, decimal>();
        foreach (var (cat, amt) in transactions)
            dict[cat] = dict.GetValueOrDefault(cat) + amt;
        foreach (var (cat, amt) in recurring)
            dict[cat] = dict.GetValueOrDefault(cat) + amt;
        return dict.OrderByDescending(x => x.Value).Select(x => (x.Key, x.Value)).ToList();
    }

    private static IReadOnlyList<(int Year, int Month, decimal Income, decimal Expenses)> MergeTrendWithRecurring(
        IReadOnlyList<(int Year, int Month, decimal Income, decimal Expenses)> trend,
        IReadOnlyList<(int Year, int Month, decimal Income, decimal Expenses)> recurring)
    {
        var trendByMonth = trend
            .GroupBy(x => (x.Year, x.Month))
            .ToDictionary(g => g.Key, g => (Income: g.Sum(x => x.Income), Expenses: g.Sum(x => x.Expenses)));
        var recurringByMonth = recurring
            .GroupBy(x => (x.Year, x.Month))
            .ToDictionary(g => g.Key, g => (Income: g.Sum(x => x.Income), Expenses: g.Sum(x => x.Expenses)));

        var allKeys = trendByMonth.Keys.Union(recurringByMonth.Keys).OrderBy(k => k.Item1).ThenBy(k => k.Item2).ToList();
        return allKeys.Select(k =>
        {
            var (ti, te) = trendByMonth.TryGetValue(k, out var tv) ? (tv.Income, tv.Expenses) : (0m, 0m);
            var (ri, re) = recurringByMonth.TryGetValue(k, out var rv) ? (rv.Income, rv.Expenses) : (0m, 0m);
            return (k.Item1, k.Item2, ti + ri, te + re);
        }).ToList();
    }

    private async Task<bool> UserBelongsToHouseholdAsync(Guid userId, Guid householdId, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        return user != null && user.HouseholdId.HasValue && user.HouseholdId.Value == householdId;
    }

    private static DashboardDto CreateEmptyDashboard(int? year, int? month)
    {
        var now = DateTime.UtcNow;
        int monthDisplay;
        if (month == null)
            monthDisplay = now.Month;
        else if (month == -1 || month == 0 || month is >= 1 and <= 12)
            monthDisplay = month.Value;
        else
            monthDisplay = now.Month;

        return new DashboardDto
        {
            TotalBalance = 0,
            Currency = "EUR",
            Year = year == 0 ? 0 : (year ?? now.Year),
            Month = monthDisplay,
            MonthlyIncome = 0,
            MonthlyExpenses = 0,
            ExpensesByCategory = Array.Empty<ExpenseByCategoryDto>(),
            IncomeByCategory = Array.Empty<IncomeByCategoryDto>(),
            MonthlyTrend = Array.Empty<MonthlyTrendDto>()
        };
    }

    private static IReadOnlyList<ExpenseByCategoryDto> BuildExpensesByCategory(IReadOnlyList<(int Category, decimal Amount)> data, decimal totalExpenses)
    {
        if (data.Count == 0) return Array.Empty<ExpenseByCategoryDto>();

        var total = totalExpenses > 0 ? totalExpenses : 1m;
        return data.Select(x => new ExpenseByCategoryDto
        {
            Category = (TransactionCategory)x.Category,
            CategoryName = GetCategoryName((TransactionCategory)x.Category),
            Amount = x.Amount,
            Percentage = Math.Round(x.Amount / total * 100, 1)
        }).ToList();
    }

    private static IReadOnlyList<IncomeByCategoryDto> BuildIncomeByCategory(IReadOnlyList<(int Category, decimal Amount)> data, decimal totalIncome)
    {
        if (data.Count == 0) return Array.Empty<IncomeByCategoryDto>();

        var total = totalIncome > 0 ? totalIncome : 1m;
        return data.Select(x => new IncomeByCategoryDto
        {
            Category = (TransactionCategory)x.Category,
            CategoryName = GetCategoryName((TransactionCategory)x.Category),
            Amount = x.Amount,
            Percentage = Math.Round(x.Amount / total * 100, 1)
        }).ToList();
    }

    private static IReadOnlyList<MonthlyTrendDto> BuildMonthlyTrend(IReadOnlyList<(int Year, int Month, decimal Income, decimal Expenses)> data)
    {
        var monthNames = new[] { "", "Jan", "Fev", "Mar", "Abr", "Mai", "Jun", "Jul", "Ago", "Set", "Out", "Nov", "Dez" };
        return data.Select(x =>
        {
            var m = Math.Clamp(x.Month, 1, 12);
            return new MonthlyTrendDto
            {
                Year = x.Year,
                Month = x.Month,
                Label = $"{monthNames[m]} {x.Year}",
                Income = x.Income,
                Expenses = x.Expenses
            };
        }).ToList();
    }

    private static string GetCategoryName(TransactionCategory category) => category switch
    {
        TransactionCategory.Salary => "Salário",
        TransactionCategory.Freelance => "Freelance",
        TransactionCategory.Investment => "Investimento",
        TransactionCategory.Gift => "Presente",
        TransactionCategory.Refund => "Reembolso",
        TransactionCategory.Food => "Alimentação",
        TransactionCategory.Transport => "Transportes",
        TransactionCategory.Housing => "Habitação",
        TransactionCategory.Utilities => "Utilidades",
        TransactionCategory.Health => "Saúde",
        TransactionCategory.Entertainment => "Entretenimento",
        TransactionCategory.Shopping => "Compras",
        TransactionCategory.Education => "Educação",
        TransactionCategory.Transfer => "Transferência",
        _ => "Outro"
    };

    private async Task<IReadOnlyList<AccountBalanceAtDate>> MergeRecurringIntoAccountBalancesAsync(
        Guid householdId,
        IReadOnlyList<AccountBalanceAtDate> balances,
        int periodThroughYear,
        int periodThroughMonth,
        CancellationToken cancellationToken)
    {
        var adj = await _recurringAccountBalanceService.GetCumulativeRecurringNetThroughMonthAsync(
            householdId, periodThroughYear, periodThroughMonth, cancellationToken);
        return balances.Select(b => b with { Balance = b.Balance + adj.GetValueOrDefault(b.AccountId) }).ToList();
    }
}
