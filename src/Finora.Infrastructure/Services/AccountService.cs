using Finora.Application.DTOs.Account;
using Finora.Application.Interfaces;
using Finora.Domain.Entities;
using Finora.Domain.Enums;

namespace Finora.Infrastructure.Services;

public class AccountService : IAccountService
{
    private readonly IAccountRepository _accountRepository;
    private readonly IUserRepository _userRepository;
    private readonly IRecurringAccountBalanceService _recurringAccountBalanceService;

    public AccountService(
        IAccountRepository accountRepository,
        IUserRepository userRepository,
        IRecurringAccountBalanceService recurringAccountBalanceService)
    {
        _accountRepository = accountRepository;
        _userRepository = userRepository;
        _recurringAccountBalanceService = recurringAccountBalanceService;
    }

    public async Task<IReadOnlyList<AccountDto>> GetByHouseholdAsync(Guid householdId, Guid userId, CancellationToken cancellationToken = default)
    {
        if (!await UserBelongsToHouseholdAsync(userId, householdId, cancellationToken))
            return Array.Empty<AccountDto>();

        var accounts = await _accountRepository.GetByHouseholdIdAsync(householdId, cancellationToken);
        var now = DateTime.UtcNow;
        var recurringNet = await _recurringAccountBalanceService.GetCumulativeRecurringNetThroughMonthAsync(
            householdId, now.Year, now.Month, cancellationToken);
        return accounts
            .Select(a => ToDto(a, recurringNet.GetValueOrDefault(a.Id)))
            .ToList();
    }

    public async Task<AccountDto?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var account = await _accountRepository.GetByIdAsync(id, cancellationToken);
        if (account == null) return null;

        if (!await UserBelongsToHouseholdAsync(userId, account.HouseholdId, cancellationToken))
            return null;

        var now = DateTime.UtcNow;
        var recurringNet = await _recurringAccountBalanceService.GetCumulativeRecurringNetThroughMonthAsync(
            account.HouseholdId, now.Year, now.Month, cancellationToken);
        return ToDto(account, recurringNet.GetValueOrDefault(account.Id));
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
        var now = DateTime.UtcNow;
        var recurringNet = await _recurringAccountBalanceService.GetCumulativeRecurringNetThroughMonthAsync(
            householdId, now.Year, now.Month, cancellationToken);
        return ToDto(account, recurringNet.GetValueOrDefault(account.Id));
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
        var now = DateTime.UtcNow;
        var recurringNet = await _recurringAccountBalanceService.GetCumulativeRecurringNetThroughMonthAsync(
            account.HouseholdId, now.Year, now.Month, cancellationToken);
        return ToDto(account, recurringNet.GetValueOrDefault(account.Id));
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

    private static AccountDto ToDto(Account account, decimal recurringNetThroughCurrentMonth)
    {
        return new AccountDto
        {
            Id = account.Id,
            Name = account.Name,
            Type = account.Type,
            Balance = account.Balance + recurringNetThroughCurrentMonth,
            Currency = account.Currency,
            HouseholdId = account.HouseholdId
        };
    }
}
