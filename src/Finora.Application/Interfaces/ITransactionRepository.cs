using Finora.Domain.Entities;

namespace Finora.Application.Interfaces;

public interface ITransactionRepository
{
    Task<Transaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Transaction?> GetByIdTrackedAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Transaction>> GetByHouseholdAsync(Guid householdId, Guid? accountId, DateTime? from, DateTime? to, CancellationToken cancellationToken = default);
    /// <summary>Earliest transaction date per account for the household (for recurring balance window).</summary>
    Task<IReadOnlyDictionary<Guid, DateTime>> GetMinTransactionDateByAccountAsync(Guid householdId, CancellationToken cancellationToken = default);
    Task<Transaction> CreateAsync(Transaction transaction, CancellationToken cancellationToken = default);
    Task<Transaction> UpdateAsync(Transaction transaction, CancellationToken cancellationToken = default);
    Task DeleteAsync(Transaction transaction, CancellationToken cancellationToken = default);
    Task<int> CountByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default);
    Task ReassignAccountAsync(Guid fromAccountId, Guid toAccountId, CancellationToken cancellationToken = default);
}
