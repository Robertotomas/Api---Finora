using Finora.Domain.Enums;

namespace Finora.Application.DTOs.Dashboard;

public record DashboardDto
{
    public decimal TotalBalance { get; init; }
    public string Currency { get; init; } = "EUR";
    public int Year { get; init; }
    public int Month { get; init; }
    public decimal MonthlyIncome { get; init; }
    public decimal MonthlyExpenses { get; init; }
    public decimal MonthlySavings => MonthlyIncome - MonthlyExpenses;
    public IReadOnlyList<ExpenseByCategoryDto> ExpensesByCategory { get; init; } = Array.Empty<ExpenseByCategoryDto>();
    public IReadOnlyList<MonthlyTrendDto> MonthlyTrend { get; init; } = Array.Empty<MonthlyTrendDto>();
}

public record ExpenseByCategoryDto
{
    public TransactionCategory Category { get; init; }
    public string CategoryName { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public decimal Percentage { get; init; }
}

public record MonthlyTrendDto
{
    public int Year { get; init; }
    public int Month { get; init; }
    public string Label { get; init; } = string.Empty;
    public decimal Income { get; init; }
    public decimal Expenses { get; init; }
    public decimal Savings => Income - Expenses;
}
