namespace Finora.Application.Interfaces;

public interface IDashboardRepository
{
    Task<decimal> GetTotalBalanceAsync(Guid householdId, CancellationToken cancellationToken = default);
    Task<decimal> GetMonthlyIncomeAsync(Guid householdId, int year, int month, CancellationToken cancellationToken = default);
    Task<decimal> GetMonthlyExpensesAsync(Guid householdId, int year, int month, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<(int Category, decimal Amount)>> GetExpensesByCategoryAsync(Guid householdId, int year, int month, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<(int Year, int Month, decimal Income, decimal Expenses)>> GetMonthlyTrendAsync(Guid householdId, int monthsBack, CancellationToken cancellationToken = default);
}
