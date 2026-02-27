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

        var splitData = await ResolveSplitsAsync(request.Splits, userId, householdId, cancellationToken);
        if (splitData == null)
            return null;

        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            AccountId = request.AccountId,
            HouseholdId = householdId,
            Type = request.Type,
            Category = request.Category,
            Amount = request.Amount,
            Date = request.Date.Kind == DateTimeKind.Utc ? request.Date : DateTime.SpecifyKind(request.Date, DateTimeKind.Utc),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        foreach (var (uid, pct) in splitData)
            transaction.Splits.Add(new TransactionSplit { TransactionId = transaction.Id, UserId = uid, Percentage = pct });

        await _transactionRepository.CreateAsync(transaction, cancellationToken);
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

        var splitData = await ResolveSplitsAsync(request.Splits, userId, transaction.HouseholdId, cancellationToken);
        if (splitData == null)
            return null;

        transaction.AccountId = request.AccountId;
        transaction.Type = request.Type;
        transaction.Category = request.Category;
        transaction.Amount = request.Amount;
        transaction.Date = request.Date.Kind == DateTimeKind.Utc ? request.Date : DateTime.SpecifyKind(request.Date, DateTimeKind.Utc);
        transaction.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        transaction.UpdatedAt = DateTime.UtcNow;

        transaction.Splits.Clear();
        foreach (var (uid, pct) in splitData)
            transaction.Splits.Add(new TransactionSplit { TransactionId = transaction.Id, UserId = uid, Percentage = pct });

        await _transactionRepository.UpdateAsync(transaction, cancellationToken);
        return ToDto(transaction);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var transaction = await _transactionRepository.GetByIdAsync(id, cancellationToken);
        if (transaction == null) return false;

        if (!await UserBelongsToHouseholdAsync(userId, transaction.HouseholdId, cancellationToken))
            return false;

        await _transactionRepository.DeleteAsync(transaction, cancellationToken);
        return true;
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
            Splits = t.Splits.Select(s => new TransactionSplitDto { UserId = s.UserId, Percentage = s.Percentage }).ToList()
        };
    }
}
