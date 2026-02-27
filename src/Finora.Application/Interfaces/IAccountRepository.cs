using Finora.Domain.Entities;

namespace Finora.Application.Interfaces;

public interface IAccountRepository
{
    Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Account>> GetByHouseholdIdAsync(Guid householdId, CancellationToken cancellationToken = default);
    Task<Account> CreateAsync(Account account, CancellationToken cancellationToken = default);
    Task<Account> UpdateAsync(Account account, CancellationToken cancellationToken = default);
    Task DeleteAsync(Account account, CancellationToken cancellationToken = default);
}
