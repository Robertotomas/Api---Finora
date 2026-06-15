using Finora.Application.DTOs.Transaction;
using Finora.Application.Interfaces;
using Finora.Domain.Entities;
using Finora.Domain.Enums;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Finora.Infrastructure.Services;

public class TransactionService : ITransactionService
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly IAccountRepository _accountRepository;
    private readonly IUserRepository _userRepository;
    private readonly INotificationRepository _notificationRepository;
    private readonly ApplicationDbContext _db;

    private static readonly string[] MonthNames =
    {
        "", "janeiro", "fevereiro", "março", "abril", "maio", "junho",
        "julho", "agosto", "setembro", "outubro", "novembro", "dezembro"
    };

    public TransactionService(
        ITransactionRepository transactionRepository,
        IAccountRepository accountRepository,
        IUserRepository userRepository,
        INotificationRepository notificationRepository,
        ApplicationDbContext db)
    {
        _transactionRepository = transactionRepository;
        _accountRepository = accountRepository;
        _userRepository = userRepository;
        _notificationRepository = notificationRepository;
        _db = db;
    }

    public async Task<IReadOnlyList<TransactionDto>> GetByHouseholdAsync(Guid householdId, Guid userId, Guid? accountId, DateTime? from, DateTime? to, int? limit = null, CancellationToken cancellationToken = default)
    {
        if (!await UserBelongsToHouseholdAsync(userId, householdId, cancellationToken))
            return Array.Empty<TransactionDto>();

        var transactions = await _transactionRepository.GetByHouseholdAsync(householdId, accountId, from, to, limit, cancellationToken);
        return transactions.Select(ToDto).ToList();
    }

    public async Task<(IReadOnlyList<TransactionDto> Items, int TotalCount)> GetByHouseholdPagedAsync(Guid householdId, Guid userId, Guid? accountId, DateTime? from, DateTime? to, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (!await UserBelongsToHouseholdAsync(userId, householdId, cancellationToken))
            return (Array.Empty<TransactionDto>(), 0);

        var (items, totalCount) = await _transactionRepository.GetByHouseholdPagedAsync(householdId, accountId, from, to, page, pageSize, cancellationToken);
        return (items.Select(ToDto).ToList(), totalCount);
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
            EntityType = request.Type == TransactionType.Transfer ? TransactionEntityType.Entity : request.EntityType,
            EntityName = request.Type == TransactionType.Transfer || string.IsNullOrWhiteSpace(request.EntityName) ? null : request.EntityName.Trim(),
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

        if (request.Type == TransactionType.Expense)
            await CheckBudgetExceededAsync(householdId, transaction.Date, cancellationToken);

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
        var oldDate = transaction.Date;

        transaction.AccountId = request.AccountId;
        transaction.Type = request.Type;
        transaction.Category = request.Type == TransactionType.Transfer ? TransactionCategory.Transfer : request.Category;
        transaction.Amount = request.Amount;
        transaction.Date = request.Date.Kind == DateTimeKind.Utc ? request.Date : DateTime.SpecifyKind(request.Date, DateTimeKind.Utc);
        transaction.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        transaction.EntityType = request.Type == TransactionType.Transfer ? TransactionEntityType.Entity : request.EntityType;
        transaction.EntityName = request.Type == TransactionType.Transfer || string.IsNullOrWhiteSpace(request.EntityName) ? null : request.EntityName.Trim();
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

        // Check budget notifications for affected months
        if (request.Type == TransactionType.Expense)
            await CheckBudgetExceededAsync(transaction.HouseholdId, transaction.Date, cancellationToken);
        if (oldType == TransactionType.Expense)
            await ClearBudgetExceededIfBelowAsync(transaction.HouseholdId, oldDate, cancellationToken);

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

        var wasExpense = transaction.Type == TransactionType.Expense;
        var txDate = transaction.Date;
        var txHouseholdId = transaction.HouseholdId;

        await _transactionRepository.DeleteAsync(transaction, cancellationToken);

        if (wasExpense)
            await ClearBudgetExceededIfBelowAsync(txHouseholdId, txDate, cancellationToken);

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

    private async Task CheckBudgetExceededAsync(Guid householdId, DateTime transactionDate, CancellationToken ct)
    {
        var year = transactionDate.Year;
        var month = transactionDate.Month;

        var budget = await _db.MonthlyBudgets
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.HouseholdId == householdId && b.Year == year && b.Month == month, ct);

        if (budget == null || budget.ExpectedExpenses <= 0)
            return;

        var dedupKey = $"budget-exceeded:{householdId}:{year}:{month}";
        if (await _notificationRepository.ExistsByDeduplicationKeyAsync(dedupKey, ct))
            return;

        var totalExpenses = await _db.Transactions
            .AsNoTracking()
            .Where(t => t.HouseholdId == householdId
                && t.Type == TransactionType.Expense
                && t.Date.Year == year
                && t.Date.Month == month)
            .SumAsync(t => t.Amount, ct);

        if (totalExpenses <= budget.ExpectedExpenses)
            return;

        await _notificationRepository.AddAsync(new Notification
        {
            Id = Guid.NewGuid(),
            HouseholdId = householdId,
            Type = NotificationType.BudgetExceeded,
            Message = $"As despesas de {MonthNames[month]} ultrapassaram o orçamento de {budget.ExpectedExpenses:N2}€.",
            RedirectUrl = "/transactions?tab=dashboard",
            DeduplicationKey = dedupKey,
            CreatedAt = DateTime.UtcNow
        }, ct);
    }

    private async Task ClearBudgetExceededIfBelowAsync(Guid householdId, DateTime transactionDate, CancellationToken ct)
    {
        var year = transactionDate.Year;
        var month = transactionDate.Month;

        var dedupKey = $"budget-exceeded:{householdId}:{year}:{month}";
        if (!await _notificationRepository.ExistsByDeduplicationKeyAsync(dedupKey, ct))
            return;

        var budget = await _db.MonthlyBudgets
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.HouseholdId == householdId && b.Year == year && b.Month == month, ct);

        if (budget == null || budget.ExpectedExpenses <= 0)
            return;

        var totalExpenses = await _db.Transactions
            .AsNoTracking()
            .Where(t => t.HouseholdId == householdId
                && t.Type == TransactionType.Expense
                && t.Date.Year == year
                && t.Date.Month == month)
            .SumAsync(t => t.Amount, ct);

        if (totalExpenses <= budget.ExpectedExpenses)
            await _notificationRepository.DeleteByDeduplicationKeyAsync(dedupKey, ct);
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
            EntityType = t.EntityType,
            EntityName = t.EntityName,
            DestinationAccountId = t.DestinationAccountId,
            Splits = t.Splits.Select(s => new TransactionSplitDto { UserId = s.UserId, Percentage = s.Percentage }).ToList()
        };
    }
}
