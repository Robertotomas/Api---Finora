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
        var targetYear = year ?? now.Year;
        var targetMonth = month ?? now.Month;

        // DbContext is not thread-safe; run queries sequentially
        var totalBalance = await _dashboardRepository.GetTotalBalanceAsync(householdId, cancellationToken);
        var monthlyIncome = await _dashboardRepository.GetMonthlyIncomeAsync(householdId, targetYear, targetMonth, cancellationToken);
        var monthlyExpenses = await _dashboardRepository.GetMonthlyExpensesAsync(householdId, targetYear, targetMonth, cancellationToken);

        var (recurringIncome, recurringExpenses) = await _recurringService.GetAmountsForMonthAsync(householdId, userId, targetYear, targetMonth, cancellationToken);
        monthlyIncome += recurringIncome;
        monthlyExpenses += recurringExpenses;

        var expensesByCategory = await _dashboardRepository.GetExpensesByCategoryAsync(householdId, targetYear, targetMonth, cancellationToken);
        var recurringByCategory = await _recurringService.GetRecurringExpensesByCategoryAsync(householdId, userId, targetYear, targetMonth, cancellationToken);
        var mergedCategories = MergeExpensesByCategory(expensesByCategory, recurringByCategory);

        var monthlyTrend = await _dashboardRepository.GetMonthlyTrendAsync(householdId, trendMonths, cancellationToken);

        var startDate = now.AddMonths(-(trendMonths - 1));
        var recurringByMonth = await _recurringService.GetAmountsByMonthAsync(householdId, userId, startDate.Year, startDate.Month, trendMonths, cancellationToken);
        var trendWithRecurring = MergeTrendWithRecurring(monthlyTrend, recurringByMonth);
        var trendFiltered = trendWithRecurring.Where(x => x.Income > 0 || x.Expenses > 0).ToList();

        var categoryDtos = BuildExpensesByCategory(mergedCategories, monthlyExpenses);
        var trendDtos = BuildMonthlyTrend(trendFiltered);

        return new DashboardDto
        {
            TotalBalance = totalBalance,
            Currency = "EUR",
            Year = targetYear,
            Month = targetMonth,
            MonthlyIncome = monthlyIncome,
            MonthlyExpenses = monthlyExpenses,
            ExpensesByCategory = categoryDtos,
            MonthlyTrend = trendDtos
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
            Year = year ?? now.Year,
            Month = month ?? now.Month,
            MonthlyIncome = 0,
            MonthlyExpenses = 0,
            ExpensesByCategory = Array.Empty<ExpenseByCategoryDto>(),
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

    private static IReadOnlyList<MonthlyTrendDto> BuildMonthlyTrend(IReadOnlyList<(int Year, int Month, decimal Income, decimal Expenses)> data)
    {
        var monthNames = new[] { "", "Jan", "Fev", "Mar", "Abr", "Mai", "Jun", "Jul", "Ago", "Set", "Out", "Nov", "Dez" };
        return data.Select(x => new MonthlyTrendDto
        {
            Year = x.Year,
            Month = x.Month,
            Label = $"{monthNames[x.Month]} {x.Year}",
            Income = x.Income,
            Expenses = x.Expenses
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
