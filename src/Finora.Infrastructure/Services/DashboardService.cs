using Finora.Application.DTOs.Dashboard;
using Finora.Application.Interfaces;
using Finora.Domain.Enums;

namespace Finora.Infrastructure.Services;

public class DashboardService : IDashboardService
{
    private readonly IDashboardRepository _dashboardRepository;
    private readonly IRecurringTransactionService _recurringService;
    private readonly IUserRepository _userRepository;

    public DashboardService(
        IDashboardRepository dashboardRepository,
        IRecurringTransactionService recurringService,
        IUserRepository userRepository)
    {
        _dashboardRepository = dashboardRepository;
        _recurringService = recurringService;
        _userRepository = userRepository;
    }

    public async Task<DashboardDto> GetDashboardAsync(Guid householdId, Guid userId, int? year, int? month, int trendMonths = 6, CancellationToken cancellationToken = default)
    {
        if (!await UserBelongsToHouseholdAsync(userId, householdId, cancellationToken))
            return CreateEmptyDashboard(year, month);

        var now = DateTime.UtcNow;
        // year=0 means "all time" - must be handled before any DateTime(year, ...) which throws for year 0
        var useAllTime = year is 0;
        var targetYear = useAllTime ? now.Year : (year ?? now.Year);
        var useFullYear = !month.HasValue || month.Value == 0;

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

        var totalBalance = accountBalancesAtPeriod.Sum(a => a.Balance);

        var effectiveTrendMonths = useAllTime ? Math.Max(trendMonths, 60) : trendMonths;
        var monthlyTrend = await _dashboardRepository.GetMonthlyTrendAsync(householdId, effectiveTrendMonths, cancellationToken);

        var startDate = now.AddMonths(-(effectiveTrendMonths - 1));
        var recurringByMonth = useAllTime
            ? new List<(int Year, int Month, decimal Income, decimal Expenses)>()
            : await _recurringService.GetAmountsByMonthAsync(householdId, userId, startDate.Year, startDate.Month, trendMonths, cancellationToken);
        var trendWithRecurring = useAllTime ? monthlyTrend : MergeTrendWithRecurring(monthlyTrend, recurringByMonth);
        var trendFiltered = trendWithRecurring.Where(x => x.Income > 0 || x.Expenses > 0).ToList();

        var categoryDtos = BuildExpensesByCategory(mergedCategories, monthlyExpenses);
        var incomeCategoryDtos = BuildIncomeByCategory(mergedIncomeCategories, monthlyIncome);
        var trendDtos = BuildMonthlyTrend(trendFiltered);

        return new DashboardDto
        {
            TotalBalance = totalBalance,
            Currency = "EUR",
            Year = useAllTime ? 0 : targetYear,
            Month = useAllTime || useFullYear ? 0 : month!.Value,
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
        var trendDict = trend.ToDictionary(x => (x.Year, x.Month));
        return recurring.Select(r =>
        {
            var (ti, te) = trendDict.TryGetValue((r.Year, r.Month), out var t) ? (t.Income, t.Expenses) : (0m, 0m);
            return (r.Year, r.Month, ti + r.Income, te + r.Expenses);
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
        return new DashboardDto
        {
            TotalBalance = 0,
            Currency = "EUR",
            Year = year == 0 ? 0 : (year ?? now.Year),
            Month = month == 0 ? 0 : (month ?? now.Month),
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
        _ => "Outro"
    };
}
