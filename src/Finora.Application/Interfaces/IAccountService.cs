using Finora.Application.DTOs.Account;

namespace Finora.Application.Interfaces;

public interface IAccountService
{
    Task<IReadOnlyList<AccountDto>> GetByHouseholdAsync(Guid householdId, Guid userId, CancellationToken cancellationToken = default);
    Task<AccountDto?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
    Task<AccountDto?> CreateAsync(CreateAccountRequest request, Guid householdId, Guid userId, CancellationToken cancellationToken = default);
    Task<AccountDto?> UpdateAsync(Guid id, UpdateAccountRequest request, Guid userId, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
}
