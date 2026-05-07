namespace Finora.Application.DTOs.Budget;

public record MonthlyBudgetDto
{
    public Guid Id { get; init; }
    public int Year { get; init; }
    public int Month { get; init; }
    public decimal ExpectedIncome { get; init; }
    public decimal ExpectedExpenses { get; init; }
}
