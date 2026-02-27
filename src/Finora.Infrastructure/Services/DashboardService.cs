using Finora.Application.DTOs.Dashboard;
using Finora.Application.Interfaces;
using Finora.Domain.Enums;

namespace Finora.Infrastructure.Services;

public class DashboardService : IDashboardService
{
    private readonly IDashboardRepository _dashboardRepository;
    private readonly IUserRepository _userRepository;

    public DashboardService(IDashboardRepository dashboardRepository, IUserRepository userRepository)
    {
        _dashboardRepository = dashboardRepository;
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
        var expensesByCategory = await _dashboardRepository.GetExpensesByCategoryAsync(householdId, targetYear, targetMonth, cancellationToken);
        var monthlyTrend = await _dashboardRepository.GetMonthlyTrendAsync(householdId, trendMonths, cancellationToken);

        var categoryDtos = BuildExpensesByCategory(expensesByCategory, monthlyExpenses);
        var trendDtos = BuildMonthlyTrend(monthlyTrend);

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
