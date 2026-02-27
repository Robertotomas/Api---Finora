using Finora.Application.DTOs.Account;
using Finora.Application.Interfaces;
using Finora.Domain.Entities;
using Finora.Domain.Enums;

namespace Finora.Infrastructure.Services;

public class AccountService : IAccountService
{
    private readonly IAccountRepository _accountRepository;
    private readonly IUserRepository _userRepository;

    public AccountService(IAccountRepository accountRepository, IUserRepository userRepository)
    {
        _accountRepository = accountRepository;
        _userRepository = userRepository;
    }

    public async Task<IReadOnlyList<AccountDto>> GetByHouseholdAsync(Guid householdId, Guid userId, CancellationToken cancellationToken = default)
    {
        if (!await UserBelongsToHouseholdAsync(userId, householdId, cancellationToken))
            return Array.Empty<AccountDto>();

        var accounts = await _accountRepository.GetByHouseholdIdAsync(householdId, cancellationToken);
        return accounts.Select(ToDto).ToList();
    }

    public async Task<AccountDto?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var account = await _accountRepository.GetByIdAsync(id, cancellationToken);
        if (account == null) return null;

        if (!await UserBelongsToHouseholdAsync(userId, account.HouseholdId, cancellationToken))
            return null;

        return ToDto(account);
    }

    public async Task<AccountDto?> CreateAsync(CreateAccountRequest request, Guid householdId, Guid userId, CancellationToken cancellationToken = default)
    {
        if (!await UserBelongsToHouseholdAsync(userId, householdId, cancellationToken))
            return null;

        var account = new Account
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Type = request.Type,
            Balance = request.Balance,
            Currency = request.Currency.Trim().ToUpperInvariant(),
            HouseholdId = householdId,
            CreatedAt = DateTime.UtcNow
        };

        await _accountRepository.CreateAsync(account, cancellationToken);
        return ToDto(account);
    }

    public async Task<AccountDto?> UpdateAsync(Guid id, UpdateAccountRequest request, Guid userId, CancellationToken cancellationToken = default)
    {
        var account = await _accountRepository.GetByIdAsync(id, cancellationToken);
        if (account == null) return null;

        if (!await UserBelongsToHouseholdAsync(userId, account.HouseholdId, cancellationToken))
            return null;

        account.Name = request.Name.Trim();
        account.Type = request.Type;
        account.Balance = request.Balance;
        account.Currency = request.Currency.Trim().ToUpperInvariant();
        account.UpdatedAt = DateTime.UtcNow;

        await _accountRepository.UpdateAsync(account, cancellationToken);
        return ToDto(account);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var account = await _accountRepository.GetByIdAsync(id, cancellationToken);
        if (account == null) return false;

        if (!await UserBelongsToHouseholdAsync(userId, account.HouseholdId, cancellationToken))
            return false;

        await _accountRepository.DeleteAsync(account, cancellationToken);
        return true;
    }

    private async Task<bool> UserBelongsToHouseholdAsync(Guid userId, Guid householdId, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        return user != null && user.HouseholdId.HasValue && user.HouseholdId.Value == householdId;
    }

    private static AccountDto ToDto(Account account)
    {
        return new AccountDto
        {
            Id = account.Id,
            Name = account.Name,
            Type = account.Type,
            Balance = account.Balance,
            Currency = account.Currency,
            HouseholdId = account.HouseholdId
        };
    }
}
