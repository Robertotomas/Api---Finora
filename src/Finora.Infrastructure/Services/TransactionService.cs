using Finora.Application.DTOs.Transaction;
using Finora.Application.Interfaces;
using Finora.Domain.Entities;
using Finora.Domain.Enums;

namespace Finora.Infrastructure.Services;

public class TransactionService : ITransactionService
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly IAccountRepository _accountRepository;
    private readonly IUserRepository _userRepository;

    public TransactionService(
        ITransactionRepository transactionRepository,
        IAccountRepository accountRepository,
        IUserRepository userRepository)
    {
        _transactionRepository = transactionRepository;
        _accountRepository = accountRepository;
        _userRepository = userRepository;
    }

    public async Task<IReadOnlyList<TransactionDto>> GetByHouseholdAsync(Guid householdId, Guid userId, Guid? accountId, DateTime? from, DateTime? to, CancellationToken cancellationToken = default)
    {
        if (!await UserBelongsToHouseholdAsync(userId, householdId, cancellationToken))
            return Array.Empty<TransactionDto>();

        var transactions = await _transactionRepository.GetByHouseholdAsync(householdId, accountId, from, to, cancellationToken);
        return transactions.Select(ToDto).ToList();
    }

    public async Task<TransactionDto?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var transaction = await _transactionRepository.GetByIdAsync(id, cancellationToken);
        if (transaction == null) return null;

        if (!await UserBelongsToHouseholdAsync(userId, transaction.HouseholdId, cancellationToken))
            return null;

        return ToDto(transaction);
    }

    public async Task<TransactionDto?> CreateAsync(CreateTransactionRequest request, Guid householdId, Guid userId, CancellationToken cancellationToken = default)
    {
        if (!await UserBelongsToHouseholdAsync(userId, householdId, cancellationToken))
            return null;

        var account = await _accountRepository.GetByIdAsync(request.AccountId, cancellationToken);
        if (account == null || account.HouseholdId != householdId)
            return null;

        // Transfer validation
        Account? destinationAccount = null;
        if (request.Type == TransactionType.Transfer)
        {
            if (!request.DestinationAccountId.HasValue)
                return null;
            if (request.DestinationAccountId.Value == request.AccountId)
                return null;
            destinationAccount = await _accountRepository.GetByIdAsync(request.DestinationAccountId.Value, cancellationToken);
            if (destinationAccount == null || destinationAccount.HouseholdId != householdId)
                return null;
        }

        var splitData = await ResolveSplitsAsync(request.Splits, userId, householdId, cancellationToken);
        if (splitData == null)
            return null;

        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            AccountId = request.AccountId,
            HouseholdId = householdId,
            Type = request.Type,
            Category = request.Type == TransactionType.Transfer ? TransactionCategory.Transfer : request.Category,
            Amount = request.Amount,
            Date = request.Date.Kind == DateTimeKind.Utc ? request.Date : DateTime.SpecifyKind(request.Date, DateTimeKind.Utc),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            DestinationAccountId = request.Type == TransactionType.Transfer ? request.DestinationAccountId : null,
            CreatedAt = DateTime.UtcNow
        };

        foreach (var (uid, pct) in splitData)
            transaction.Splits.Add(new TransactionSplit { TransactionId = transaction.Id, UserId = uid, Percentage = pct });

        await _transactionRepository.CreateAsync(transaction, cancellationToken);

        if (request.Type == TransactionType.Transfer)
        {
            account.Balance -= request.Amount;
            await _accountRepository.UpdateAsync(account, cancellationToken);
            destinationAccount!.Balance += request.Amount;
            await _accountRepository.UpdateAsync(destinationAccount, cancellationToken);
        }
        else
        {
            account.Balance = request.Type == TransactionType.Income
                ? account.Balance + request.Amount
                : account.Balance - request.Amount;
            await _accountRepository.UpdateAsync(account, cancellationToken);
        }

        return ToDto(transaction);
    }

    public async Task<TransactionDto?> UpdateAsync(Guid id, UpdateTransactionRequest request, Guid userId, CancellationToken cancellationToken = default)
    {
        var transaction = await _transactionRepository.GetByIdTrackedAsync(id, cancellationToken);
        if (transaction == null) return null;

        if (!await UserBelongsToHouseholdAsync(userId, transaction.HouseholdId, cancellationToken))
            return null;

        var account = await _accountRepository.GetByIdAsync(request.AccountId, cancellationToken);
        if (account == null || account.HouseholdId != transaction.HouseholdId)
            return null;

        // Transfer validation for new values
        Account? newDestAccount = null;
        if (request.Type == TransactionType.Transfer)
        {
            if (!request.DestinationAccountId.HasValue)
                return null;
            if (request.DestinationAccountId.Value == request.AccountId)
                return null;
            newDestAccount = await _accountRepository.GetByIdAsync(request.DestinationAccountId.Value, cancellationToken);
            if (newDestAccount == null || newDestAccount.HouseholdId != transaction.HouseholdId)
                return null;
        }

        var splitData = await ResolveSplitsAsync(request.Splits, userId, transaction.HouseholdId, cancellationToken);
        if (splitData == null)
            return null;

        var oldAccountId = transaction.AccountId;
        var oldType = transaction.Type;
        var oldAmount = transaction.Amount;
        var oldDestAccountId = transaction.DestinationAccountId;

        transaction.AccountId = request.AccountId;
        transaction.Type = request.Type;
        transaction.Category = request.Type == TransactionType.Transfer ? TransactionCategory.Transfer : request.Category;
        transaction.Amount = request.Amount;
        transaction.Date = request.Date.Kind == DateTimeKind.Utc ? request.Date : DateTime.SpecifyKind(request.Date, DateTimeKind.Utc);
        transaction.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        transaction.DestinationAccountId = request.Type == TransactionType.Transfer ? request.DestinationAccountId : null;
        transaction.UpdatedAt = DateTime.UtcNow;

        transaction.Splits.Clear();
        foreach (var (uid, pct) in splitData)
            transaction.Splits.Add(new TransactionSplit { TransactionId = transaction.Id, UserId = uid, Percentage = pct });

        await _transactionRepository.UpdateAsync(transaction, cancellationToken);

        // Revert old balance effects
        await RevertBalanceEffects(oldAccountId, oldType, oldAmount, oldDestAccountId, cancellationToken);

        // Apply new balance effects
        await ApplyBalanceEffects(request.AccountId, request.Type, request.Amount,
            request.Type == TransactionType.Transfer ? request.DestinationAccountId : null, cancellationToken);

        return ToDto(transaction);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var transaction = await _transactionRepository.GetByIdAsync(id, cancellationToken);
        if (transaction == null) return false;

        if (!await UserBelongsToHouseholdAsync(userId, transaction.HouseholdId, cancellationToken))
            return false;

        // Revert balance effects
        await RevertBalanceEffects(transaction.AccountId, transaction.Type, transaction.Amount,
            transaction.DestinationAccountId, cancellationToken);

        await _transactionRepository.DeleteAsync(transaction, cancellationToken);
        return true;
    }

    private async Task RevertBalanceEffects(Guid accountId, TransactionType type, decimal amount, Guid? destAccountId, CancellationToken cancellationToken)
    {
        var sourceAccount = await _accountRepository.GetByIdAsync(accountId, cancellationToken);
        if (sourceAccount != null)
        {
            if (type == TransactionType.Transfer)
                sourceAccount.Balance += amount;
            else if (type == TransactionType.Income)
                sourceAccount.Balance -= amount;
            else
                sourceAccount.Balance += amount;
            await _accountRepository.UpdateAsync(sourceAccount, cancellationToken);
        }

        if (type == TransactionType.Transfer && destAccountId.HasValue)
        {
            var destAccount = await _accountRepository.GetByIdAsync(destAccountId.Value, cancellationToken);
            if (destAccount != null)
            {
                destAccount.Balance -= amount;
                await _accountRepository.UpdateAsync(destAccount, cancellationToken);
            }
        }
    }

    private async Task ApplyBalanceEffects(Guid accountId, TransactionType type, decimal amount, Guid? destAccountId, CancellationToken cancellationToken)
    {
        var sourceAccount = await _accountRepository.GetByIdAsync(accountId, cancellationToken);
        if (sourceAccount != null)
        {
            if (type == TransactionType.Transfer)
                sourceAccount.Balance -= amount;
            else if (type == TransactionType.Income)
                sourceAccount.Balance += amount;
            else
                sourceAccount.Balance -= amount;
            await _accountRepository.UpdateAsync(sourceAccount, cancellationToken);
        }

        if (type == TransactionType.Transfer && destAccountId.HasValue)
        {
            var destAccount = await _accountRepository.GetByIdAsync(destAccountId.Value, cancellationToken);
            if (destAccount != null)
            {
                destAccount.Balance += amount;
                await _accountRepository.UpdateAsync(destAccount, cancellationToken);
            }
        }
    }

    private async Task<bool> UserBelongsToHouseholdAsync(Guid userId, Guid householdId, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        return user != null && user.HouseholdId.HasValue && user.HouseholdId.Value == householdId;
    }

    private async Task<List<(Guid UserId, decimal Percentage)>?> ResolveSplitsAsync(IReadOnlyList<TransactionSplitInput>? input, Guid userId, Guid householdId, CancellationToken cancellationToken)
    {
        if (input == null || input.Count == 0)
            return new List<(Guid, decimal)> { (userId, 100) };

        var sum = input.Sum(s => s.Percentage);
        if (Math.Abs(sum - 100) > 0.01m)
            return null;

        var usersInHousehold = await _userRepository.GetByHouseholdIdAsync(householdId, cancellationToken);
        var userIds = usersInHousehold.Select(u => u.Id).ToHashSet();

        var splits = new List<(Guid UserId, decimal Percentage)>();
        foreach (var s in input)
        {
            if (!userIds.Contains(s.UserId) || s.Percentage <= 0 || s.Percentage > 100)
                return null;
            splits.Add((s.UserId, s.Percentage));
        }

        return splits;
    }

    private static TransactionDto ToDto(Transaction t)
    {
        return new TransactionDto
        {
            Id = t.Id,
            AccountId = t.AccountId,
            HouseholdId = t.HouseholdId,
            Type = t.Type,
            Category = t.Category,
            Amount = t.Amount,
            Date = t.Date,
            Description = t.Description,
            DestinationAccountId = t.DestinationAccountId,
            Splits = t.Splits.Select(s => new TransactionSplitDto { UserId = s.UserId, Percentage = s.Percentage }).ToList()
        };
    }
}
