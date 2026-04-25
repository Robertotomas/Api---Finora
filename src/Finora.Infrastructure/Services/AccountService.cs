using Finora.Application.DTOs.Account;
using Finora.Application.Interfaces;
using Finora.Domain.Entities;

namespace Finora.Infrastructure.Services;

public class AccountService : IAccountService
{
    private readonly IAccountRepository _accountRepository;
    private readonly IUserRepository _userRepository;
    private readonly IHouseholdRepository _householdRepository;
    private readonly ITransactionRepository _transactionRepository;
    private readonly IRecurringTransactionRepository _recurringTransactionRepository;
    private readonly IRecurringAccountBalanceService _recurringAccountBalanceService;
    private readonly ISubscriptionService _subscriptionService;

    public AccountService(
        IAccountRepository accountRepository,
        IUserRepository userRepository,
        IHouseholdRepository householdRepository,
        ITransactionRepository transactionRepository,
        IRecurringTransactionRepository recurringTransactionRepository,
        IRecurringAccountBalanceService recurringAccountBalanceService,
        ISubscriptionService subscriptionService)
    {
        _accountRepository = accountRepository;
        _userRepository = userRepository;
        _householdRepository = householdRepository;
        _transactionRepository = transactionRepository;
        _recurringTransactionRepository = recurringTransactionRepository;
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

        var txCount = await _transactionRepository.CountByAccountIdAsync(id, cancellationToken);
        var recurringCount = await _recurringTransactionRepository.CountByAccountIdAsync(id, cancellationToken);
        if (txCount > 0 || recurringCount > 0)
        {
            throw new InvalidOperationException(
                "Não é possível eliminar esta conta porque existem movimentos ou recorrentes associados. Remove primeiro esses movimentos e recorrentes em Movimentos se quiseres continuar.");
        }

        var householdId = account.HouseholdId;
        await _accountRepository.DeleteAsync(account, cancellationToken);
        await ApplyHouseholdPrimaryRulesAsync(householdId, cancellationToken);
        return true;
    }

    public async Task<AccountDto?> ArchiveAsync(Guid id, Guid userId, Guid? targetAccountId = null, CancellationToken cancellationToken = default)
    {
        var account = await _accountRepository.GetByIdAsync(id, cancellationToken);
        if (account == null) return null;

        if (!await UserBelongsToHouseholdAsync(userId, account.HouseholdId, cancellationToken))
            return null;

        // If account has balance, must transfer to another account first
        if (account.Balance != 0)
        {
            if (!targetAccountId.HasValue)
                throw new InvalidOperationException("Esta conta tem saldo. Escolhe uma conta de destino para transferir o saldo antes de arquivar.");

            if (targetAccountId.Value == id)
                throw new InvalidOperationException("A conta de destino não pode ser igual à conta de origem.");

            var targetAccount = await _accountRepository.GetByIdAsync(targetAccountId.Value, cancellationToken);
            if (targetAccount == null || targetAccount.HouseholdId != account.HouseholdId)
                throw new InvalidOperationException("A conta de destino não foi encontrada ou não pertence ao mesmo household.");

            if (targetAccount.IsArchived)
                throw new InvalidOperationException("Não é possível transferir para uma conta arquivada.");

            targetAccount.Balance += account.Balance;
            account.Balance = 0;
            await _accountRepository.UpdateAsync(targetAccount, cancellationToken);
        }

        account.IsArchived = true;
        account.ArchivedAt = DateTime.UtcNow;
        account.UpdatedAt = DateTime.UtcNow;

        await _accountRepository.UpdateAsync(account, cancellationToken);
        await ApplyHouseholdPrimaryRulesAsync(account.HouseholdId, cancellationToken);

        var state = await _subscriptionService.GetFreeMultiAccountStateAsync(account.HouseholdId, cancellationToken);
        var now = DateTime.UtcNow;
        var recurringNet = await _recurringAccountBalanceService.GetCumulativeRecurringNetThroughMonthAsync(
            account.HouseholdId, now.Year, now.Month, cancellationToken);
        return ToDto(account, recurringNet.GetValueOrDefault(account.Id), state);
    }

    public async Task<AccountDto?> ReactivateAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var account = await _accountRepository.GetByIdAsync(id, cancellationToken);
        if (account == null) return null;

        if (!await UserBelongsToHouseholdAsync(userId, account.HouseholdId, cancellationToken))
            return null;

        account.IsArchived = false;
        account.ArchivedAt = null;
        account.UpdatedAt = DateTime.UtcNow;

        await _accountRepository.UpdateAsync(account, cancellationToken);
        await ApplyHouseholdPrimaryRulesAsync(account.HouseholdId, cancellationToken);

        var state = await _subscriptionService.GetFreeMultiAccountStateAsync(account.HouseholdId, cancellationToken);
        var now = DateTime.UtcNow;
        var recurringNet = await _recurringAccountBalanceService.GetCumulativeRecurringNetThroughMonthAsync(
            account.HouseholdId, now.Year, now.Month, cancellationToken);
        return ToDto(account, recurringNet.GetValueOrDefault(account.Id), state);
    }

    public async Task<bool> DeleteWithTransferAsync(Guid id, Guid targetAccountId, Guid userId, CancellationToken cancellationToken = default)
    {
        var account = await _accountRepository.GetByIdAsync(id, cancellationToken);
        if (account == null) return false;

        if (!await UserBelongsToHouseholdAsync(userId, account.HouseholdId, cancellationToken))
            return false;

        if (id == targetAccountId)
            throw new InvalidOperationException("A conta de destino não pode ser igual à conta de origem.");

        var targetAccount = await _accountRepository.GetByIdAsync(targetAccountId, cancellationToken);
        if (targetAccount == null || targetAccount.HouseholdId != account.HouseholdId)
            throw new InvalidOperationException("A conta de destino não foi encontrada ou não pertence ao mesmo household.");

        if (targetAccount.IsArchived)
            throw new InvalidOperationException("Não é possível transferir para uma conta arquivada.");

        // Reassign all transactions and recurring transactions
        await _transactionRepository.ReassignAccountAsync(id, targetAccountId, cancellationToken);
        await _recurringTransactionRepository.ReassignAccountAsync(id, targetAccountId, cancellationToken);

        // Transfer balance
        targetAccount.Balance += account.Balance;
        await _accountRepository.UpdateAsync(targetAccount, cancellationToken);

        // Delete source account
        var householdId = account.HouseholdId;
        await _accountRepository.DeleteAsync(account, cancellationToken);
        await ApplyHouseholdPrimaryRulesAsync(householdId, cancellationToken);
        return true;
    }

    private async Task ApplyHouseholdPrimaryRulesAsync(Guid householdId, CancellationToken cancellationToken)
    {
        var allAccounts = await _accountRepository.GetByHouseholdIdAsync(householdId, cancellationToken);
        var accounts = allAccounts.Where(a => !a.IsArchived).ToList();
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
            IsActiveForPlan = isActiveForPlan,
            IsArchived = account.IsArchived,
            ArchivedAt = account.ArchivedAt
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
