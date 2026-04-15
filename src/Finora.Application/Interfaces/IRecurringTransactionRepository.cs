using Finora.Domain.Entities;

namespace Finora.Application.Interfaces;

public interface IRecurringTransactionRepository
{
    Task<RecurringTransaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<RecurringTransaction?> GetByIdTrackedAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RecurringTransaction>> GetByHouseholdAsync(Guid householdId, CancellationToken cancellationToken = default);
    /// <summary>Gets recurring transactions active in the given month.</summary>
    Task<IReadOnlyList<RecurringTransaction>> GetActiveForMonthAsync(Guid householdId, int year, int month, CancellationToken cancellationToken = default);
    /// <summary>Gets income and expenses totals from recurring for each month in the range.</summary>
    Task<IReadOnlyList<(int Year, int Month, decimal Income, decimal Expenses)>> GetAmountsByMonthAsync(Guid householdId, int startYear, int startMonth, int count, CancellationToken cancellationToken = default);
    Task<(int Year, int Month)?> GetMinimumStartMonthAsync(Guid householdId, CancellationToken cancellationToken = default);
    Task<(decimal TotalIncome, decimal TotalExpenses, IReadOnlyList<(int Category, decimal Amount)> IncomeByCategory, IReadOnlyList<(int Category, decimal Amount)> ExpensesByCategory)> GetAggregatedForMonthRangeAsync(
        Guid householdId, int startYear, int startMonth, int endYear, int endMonth, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<(int Category, decimal Amount)>> GetRecurringExpensesByCategoryAsync(Guid householdId, int year, int month, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<(int Category, decimal Amount)>> GetRecurringIncomeByCategoryAsync(Guid householdId, int year, int month, CancellationToken cancellationToken = default);
    Task<RecurringTransaction> CreateAsync(RecurringTransaction entity, CancellationToken cancellationToken = default);
    Task<RecurringTransaction> UpdateAsync(RecurringTransaction entity, CancellationToken cancellationToken = default);
    Task<int> CountByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default);
}
