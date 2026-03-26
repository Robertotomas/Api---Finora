using Finora.Application.DTOs.RecurringTransaction;
using Finora.Application.Interfaces;
using Finora.Domain.Entities;

namespace Finora.Infrastructure.Services;

public class RecurringTransactionService : IRecurringTransactionService
{
    private readonly IRecurringTransactionRepository _repository;
    private readonly IAccountRepository _accountRepository;
    private readonly IUserRepository _userRepository;

    public RecurringTransactionService(
        IRecurringTransactionRepository repository,
        IAccountRepository accountRepository,
        IUserRepository userRepository)
    {
        _repository = repository;
        _accountRepository = accountRepository;
        _userRepository = userRepository;
    }

    public async Task<IReadOnlyList<RecurringTransactionDto>> GetByHouseholdAsync(Guid householdId, Guid userId, CancellationToken cancellationToken = default)
    {
        if (!await UserBelongsToHouseholdAsync(userId, householdId, cancellationToken))
            return Array.Empty<RecurringTransactionDto>();

        var list = await _repository.GetByHouseholdAsync(householdId, cancellationToken);
        return list.Select(ToDto).ToList();
    }

    public async Task<RecurringTransactionDto?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var entity = await _repository.GetByIdAsync(id, cancellationToken);
        if (entity == null) return null;

        if (!await UserBelongsToHouseholdAsync(userId, entity.HouseholdId, cancellationToken))
            return null;

        return ToDto(entity);
    }

    public async Task<(decimal Income, decimal Expenses)> GetAmountsForMonthAsync(Guid householdId, Guid userId, int year, int month, CancellationToken cancellationToken = default)
    {
        if (!await UserBelongsToHouseholdAsync(userId, householdId, cancellationToken))
            return (0, 0);

        var active = await _repository.GetActiveForMonthAsync(householdId, year, month, cancellationToken);
        var income = active.Where(r => r.Type == Domain.Enums.TransactionType.Income).Sum(r => r.Amount);
        var expenses = active.Where(r => r.Type == Domain.Enums.TransactionType.Expense).Sum(r => r.Amount);
        return (income, expenses);
    }

    public async Task<IReadOnlyList<(int Category, decimal Amount)>> GetRecurringExpensesByCategoryAsync(Guid householdId, Guid userId, int year, int month, CancellationToken cancellationToken = default)
    {
        if (!await UserBelongsToHouseholdAsync(userId, householdId, cancellationToken))
            return Array.Empty<(int, decimal)>();

        return await _repository.GetRecurringExpensesByCategoryAsync(householdId, year, month, cancellationToken);
    }

    public async Task<IReadOnlyList<(int Category, decimal Amount)>> GetRecurringIncomeByCategoryAsync(Guid householdId, Guid userId, int year, int month, CancellationToken cancellationToken = default)
    {
        if (!await UserBelongsToHouseholdAsync(userId, householdId, cancellationToken))
            return Array.Empty<(int, decimal)>();

        return await _repository.GetRecurringIncomeByCategoryAsync(householdId, year, month, cancellationToken);
    }

    public async Task<IReadOnlyList<(int Year, int Month, decimal Income, decimal Expenses)>> GetAmountsByMonthAsync(Guid householdId, Guid userId, int startYear, int startMonth, int count, CancellationToken cancellationToken = default)
    {
        if (!await UserBelongsToHouseholdAsync(userId, householdId, cancellationToken))
            return Array.Empty<(int, int, decimal, decimal)>();

        return await _repository.GetAmountsByMonthAsync(householdId, startYear, startMonth, count, cancellationToken);
    }

    public async Task<(int Year, int Month)?> GetMinimumRecurringStartMonthAsync(Guid householdId, Guid userId, CancellationToken cancellationToken = default)
    {
        if (!await UserBelongsToHouseholdAsync(userId, householdId, cancellationToken))
            return null;

        return await _repository.GetMinimumStartMonthAsync(householdId, cancellationToken);
    }

    public async Task<(decimal TotalIncome, decimal TotalExpenses, IReadOnlyList<(int Category, decimal Amount)> IncomeByCategory, IReadOnlyList<(int Category, decimal Amount)> ExpensesByCategory)> GetAggregatedForMonthRangeAsync(
        Guid householdId, Guid userId, int startYear, int startMonth, int endYear, int endMonth, CancellationToken cancellationToken = default)
    {
        if (!await UserBelongsToHouseholdAsync(userId, householdId, cancellationToken))
            return (0m, 0m, Array.Empty<(int, decimal)>(), Array.Empty<(int, decimal)>());

        return await _repository.GetAggregatedForMonthRangeAsync(householdId, startYear, startMonth, endYear, endMonth, cancellationToken);
    }

    public async Task<RecurringTransactionDto?> CreateAsync(CreateRecurringTransactionRequest request, Guid householdId, Guid userId, CancellationToken cancellationToken = default)
    {
        if (!await UserBelongsToHouseholdAsync(userId, householdId, cancellationToken))
            return null;

        var account = await _accountRepository.GetByIdAsync(request.AccountId, cancellationToken);
        if (account == null || account.HouseholdId != householdId)
            return null;

        var now = DateTime.UtcNow;

        var entity = new RecurringTransaction
        {
            Id = Guid.NewGuid(),
            AccountId = request.AccountId,
            HouseholdId = householdId,
            Type = request.Type,
            Category = request.Category,
            Amount = request.Amount,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            StartMonth = now.Month,
            StartYear = now.Year,
            EndMonth = null,
            EndYear = null,
            CreatedAt = now
        };

        await _repository.CreateAsync(entity, cancellationToken);
        return ToDto(entity);
    }

    public async Task<RecurringTransactionDto?> UpdateAsync(Guid id, UpdateRecurringTransactionRequest request, Guid userId, CancellationToken cancellationToken = default)
    {
        var entity = await _repository.GetByIdTrackedAsync(id, cancellationToken);
        if (entity == null) return null;

        if (!await UserBelongsToHouseholdAsync(userId, entity.HouseholdId, cancellationToken))
            return null;

        var account = await _accountRepository.GetByIdAsync(request.AccountId, cancellationToken);
        if (account == null || account.HouseholdId != entity.HouseholdId)
            return null;

        entity.AccountId = request.AccountId;
        entity.Type = request.Type;
        entity.Category = request.Category;
        entity.Amount = request.Amount;
        entity.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        entity.UpdatedAt = DateTime.UtcNow;

        await _repository.UpdateAsync(entity, cancellationToken);
        return ToDto(entity);
    }

    public async Task<bool> RemoveFromMonthAsync(Guid id, int year, int month, Guid userId, CancellationToken cancellationToken = default)
    {
        var entity = await _repository.GetByIdTrackedAsync(id, cancellationToken);
        if (entity == null) return false;

        if (!await UserBelongsToHouseholdAsync(userId, entity.HouseholdId, cancellationToken))
            return false;

        entity.EndMonth = month;
        entity.EndYear = year;
        entity.UpdatedAt = DateTime.UtcNow;

        await _repository.UpdateAsync(entity, cancellationToken);
        return true;
    }

    private async Task<bool> UserBelongsToHouseholdAsync(Guid userId, Guid householdId, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        return user != null && user.HouseholdId.HasValue && user.HouseholdId.Value == householdId;
    }

    private static RecurringTransactionDto ToDto(RecurringTransaction r)
    {
        return new RecurringTransactionDto
        {
            Id = r.Id,
            AccountId = r.AccountId,
            HouseholdId = r.HouseholdId,
            Type = r.Type,
            Category = r.Category,
            Amount = r.Amount,
            Description = r.Description,
            StartMonth = r.StartMonth,
            StartYear = r.StartYear,
            EndMonth = r.EndMonth,
            EndYear = r.EndYear
        };
    }
}
