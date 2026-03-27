using Finora.Application.DTOs.Account;
using Finora.Application.Interfaces;
using Finora.Domain.Entities;

namespace Finora.Infrastructure.Services;

public class AccountService : IAccountService
{
    private readonly IAccountRepository _accountRepository;
    private readonly IUserRepository _userRepository;
    private readonly IHouseholdRepository _householdRepository;
    private readonly IRecurringAccountBalanceService _recurringAccountBalanceService;
    private readonly ISubscriptionService _subscriptionService;

    public AccountService(
        IAccountRepository accountRepository,
        IUserRepository userRepository,
        IHouseholdRepository householdRepository,
        IRecurringAccountBalanceService recurringAccountBalanceService,
        ISubscriptionService subscriptionService)
    {
        _accountRepository = accountRepository;
        _userRepository = userRepository;
        _householdRepository = householdRepository;
        _recurringAccountBalanceService = recurringAccountBalanceService;
        _subscriptionService = subscriptionService;
    }

    public async Task<IReadOnlyList<AccountDto>> GetByHouseholdAsync(Guid householdId, Guid userId, CancellationToken cancellationToken = default)
    {
        if (!await UserBelongsToHouseholdAsync(userId, householdId, cancellationToken))
            return Array.Empty<AccountDto>();

        await ApplyHouseholdPrimaryRulesAsync(householdId, cancellationToken);

        var accounts = await _accountRepository.GetByHouseholdIdAsync(householdId, cancellationToken);
        var state = await _subscriptionService.GetFreeMultiAccountStateAsync(householdId, cancellationToken);
        var now = DateTime.UtcNow;
        var recurringNet = await _recurringAccountBalanceService.GetCumulativeRecurringNetThroughMonthAsync(
            householdId, now.Year, now.Month, cancellationToken);
        return accounts
            .Select(a => ToDto(a, recurringNet.GetValueOrDefault(a.Id), state))
            .ToList();
    }

    public async Task<AccountDto?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var account = await _accountRepository.GetByIdAsync(id, cancellationToken);
        if (account == null) return null;

        if (!await UserBelongsToHouseholdAsync(userId, account.HouseholdId, cancellationToken))
            return null;

        await ApplyHouseholdPrimaryRulesAsync(account.HouseholdId, cancellationToken);

        var state = await _subscriptionService.GetFreeMultiAccountStateAsync(account.HouseholdId, cancellationToken);
        var now = DateTime.UtcNow;
        var recurringNet = await _recurringAccountBalanceService.GetCumulativeRecurringNetThroughMonthAsync(
            account.HouseholdId, now.Year, now.Month, cancellationToken);
        return ToDto(account, recurringNet.GetValueOrDefault(account.Id), state);
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
        await ApplyHouseholdPrimaryRulesAsync(householdId, cancellationToken);

        var state = await _subscriptionService.GetFreeMultiAccountStateAsync(householdId, cancellationToken);
        var now = DateTime.UtcNow;
        var recurringNet = await _recurringAccountBalanceService.GetCumulativeRecurringNetThroughMonthAsync(
            householdId, now.Year, now.Month, cancellationToken);
        return ToDto(account, recurringNet.GetValueOrDefault(account.Id), state);
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
        await ApplyHouseholdPrimaryRulesAsync(account.HouseholdId, cancellationToken);

        var state = await _subscriptionService.GetFreeMultiAccountStateAsync(account.HouseholdId, cancellationToken);
        var now = DateTime.UtcNow;
        var recurringNet = await _recurringAccountBalanceService.GetCumulativeRecurringNetThroughMonthAsync(
            account.HouseholdId, now.Year, now.Month, cancellationToken);
        return ToDto(account, recurringNet.GetValueOrDefault(account.Id), state);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var account = await _accountRepository.GetByIdAsync(id, cancellationToken);
        if (account == null) return false;

        if (!await UserBelongsToHouseholdAsync(userId, account.HouseholdId, cancellationToken))
            return false;

        var householdId = account.HouseholdId;
        await _accountRepository.DeleteAsync(account, cancellationToken);
        await ApplyHouseholdPrimaryRulesAsync(householdId, cancellationToken);
        return true;
    }

    private async Task ApplyHouseholdPrimaryRulesAsync(Guid householdId, CancellationToken cancellationToken)
    {
        var accounts = await _accountRepository.GetByHouseholdIdAsync(householdId, cancellationToken);
        var household = await _householdRepository.GetByIdTrackedAsync(householdId, cancellationToken);
        if (household == null)
            return;

        var changed = false;
        if (accounts.Count == 0)
        {
            if (household.PrimaryAccountId != null)
            {
                household.PrimaryAccountId = null;
                changed = true;
            }
        }
        else if (accounts.Count == 1)
        {
            if (household.PrimaryAccountId != accounts[0].Id)
            {
                household.PrimaryAccountId = accounts[0].Id;
                changed = true;
            }
        }
        else if (household.PrimaryAccountId.HasValue && accounts.All(a => a.Id != household.PrimaryAccountId.Value))
        {
            household.PrimaryAccountId = null;
            changed = true;
        }

        if (changed)
        {
            household.UpdatedAt = DateTime.UtcNow;
            await _householdRepository.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<bool> UserBelongsToHouseholdAsync(Guid userId, Guid householdId, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        return user != null && user.HouseholdId.HasValue && user.HouseholdId.Value == householdId;
    }

    private static AccountDto ToDto(
        Account account,
        decimal recurringNetThroughCurrentMonth,
        (bool FreeMultiAccount, bool NeedsPrimarySelection, Guid? PrimaryAccountId) state)
    {
        var isActiveForPlan = ComputeIsActiveForPlan(state, account.Id);
        return new AccountDto
        {
            Id = account.Id,
            Name = account.Name,
            Type = account.Type,
            Balance = account.Balance + recurringNetThroughCurrentMonth,
            Currency = account.Currency,
            HouseholdId = account.HouseholdId,
            IsActiveForPlan = isActiveForPlan
        };
    }

    private static bool ComputeIsActiveForPlan(
        (bool FreeMultiAccount, bool NeedsPrimarySelection, Guid? PrimaryAccountId) state,
        Guid accountId)
    {
        if (!state.FreeMultiAccount)
            return true;
        if (state.NeedsPrimarySelection)
            return false;
        return accountId == state.PrimaryAccountId!.Value;
    }
}
