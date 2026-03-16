namespace Finora.Application.Interfaces;

public record AccountBalanceAtDate(Guid AccountId, string Name, int Type, string Currency, decimal Balance);

public interface IDashboardRepository
{
    Task<decimal> GetTotalBalanceAsync(Guid householdId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AccountBalanceAtDate>> GetAccountBalancesAtEndOfMonthAsync(Guid householdId, int year, int month, CancellationToken cancellationToken = default);
    Task<decimal> GetMonthlyIncomeAsync(Guid householdId, int year, int month, CancellationToken cancellationToken = default);
    Task<decimal> GetMonthlyExpensesAsync(Guid householdId, int year, int month, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<(int Category, decimal Amount)>> GetExpensesByCategoryAsync(Guid householdId, int year, int month, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<(int Category, decimal Amount)>> GetIncomeByCategoryAsync(Guid householdId, int year, int month, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<(int Year, int Month, decimal Income, decimal Expenses)>> GetMonthlyTrendAsync(Guid householdId, int monthsBack, CancellationToken cancellationToken = default);
    Task<decimal> GetYearlyIncomeAsync(Guid householdId, int year, CancellationToken cancellationToken = default);
    Task<decimal> GetYearlyExpensesAsync(Guid householdId, int year, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<(int Category, decimal Amount)>> GetYearlyExpensesByCategoryAsync(Guid householdId, int year, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<(int Category, decimal Amount)>> GetYearlyIncomeByCategoryAsync(Guid householdId, int year, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AccountBalanceAtDate>> GetAccountBalancesAtEndOfYearAsync(Guid householdId, int year, CancellationToken cancellationToken = default);
    Task<decimal> GetTotalIncomeAsync(Guid householdId, CancellationToken cancellationToken = default);
    Task<decimal> GetTotalExpensesAsync(Guid householdId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<(int Category, decimal Amount)>> GetTotalExpensesByCategoryAsync(Guid householdId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<(int Category, decimal Amount)>> GetTotalIncomeByCategoryAsync(Guid householdId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AccountBalanceAtDate>> GetAccountBalancesNowAsync(Guid householdId, CancellationToken cancellationToken = default);
}
