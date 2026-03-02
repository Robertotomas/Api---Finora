using Finora.Application.DTOs.RecurringTransaction;

namespace Finora.Application.Interfaces;

public interface IRecurringTransactionService
{
    Task<IReadOnlyList<RecurringTransactionDto>> GetByHouseholdAsync(Guid householdId, Guid userId, CancellationToken cancellationToken = default);
    Task<RecurringTransactionDto?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
    Task<(decimal Income, decimal Expenses)> GetAmountsForMonthAsync(Guid householdId, Guid userId, int year, int month, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<(int Category, decimal Amount)>> GetRecurringExpensesByCategoryAsync(Guid householdId, Guid userId, int year, int month, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<(int Year, int Month, decimal Income, decimal Expenses)>> GetAmountsByMonthAsync(Guid householdId, Guid userId, int startYear, int startMonth, int count, CancellationToken cancellationToken = default);
    Task<RecurringTransactionDto?> CreateAsync(CreateRecurringTransactionRequest request, Guid householdId, Guid userId, CancellationToken cancellationToken = default);
    Task<RecurringTransactionDto?> UpdateAsync(Guid id, UpdateRecurringTransactionRequest request, Guid userId, CancellationToken cancellationToken = default);
    /// <summary>Stops the recurring from this month onward (sets EndMonth/EndYear).</summary>
    Task<bool> RemoveFromMonthAsync(Guid id, int year, int month, Guid userId, CancellationToken cancellationToken = default);
}
