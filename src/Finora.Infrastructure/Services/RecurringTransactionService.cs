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
        var income = active.Where(r => r.Type == Domain.Enums.TransactionType.Income)
            .Sum(r => r.AmountForMonth(month));
        var expenses = active.Where(r => r.Type == Domain.Enums.TransactionType.Expense)
            .Sum(r => r.AmountForMonth(month));
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

        // Transfer validation
        if (request.Type == Domain.Enums.TransactionType.Transfer)
        {
            if (!request.DestinationAccountId.HasValue)
                return null;
            if (request.DestinationAccountId.Value == request.AccountId)
                return null;
            var destAccount = await _accountRepository.GetByIdAsync(request.DestinationAccountId.Value, cancellationToken);
            if (destAccount == null || destAccount.HouseholdId != householdId)
                return null;
        }

        var frequency = (Domain.Enums.RecurringFrequency)request.Frequency;
        var responsibleUserId = await ResolveResponsibleUserAsync(request.ResponsibleUserId, request.Type, householdId, cancellationToken);

        var entity = new RecurringTransaction
        {
            Id = Guid.NewGuid(),
            AccountId = request.AccountId,
            HouseholdId = householdId,
            Type = request.Type,
            Category = request.Type == Domain.Enums.TransactionType.Transfer ? Domain.Enums.TransactionCategory.Transfer : request.Category,
            Amount = request.Amount,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            EntityType = request.Type == Domain.Enums.TransactionType.Transfer ? Domain.Enums.TransactionEntityType.Entity : request.EntityType,
            EntityName = request.Type == Domain.Enums.TransactionType.Transfer || string.IsNullOrWhiteSpace(request.EntityName) ? null : request.EntityName.Trim(),
            DestinationAccountId = request.Type == Domain.Enums.TransactionType.Transfer ? request.DestinationAccountId : null,
            ResponsibleUserId = responsibleUserId,
            Frequency = frequency,
            AnnualMonth = NormalizeAnnualMonth(frequency, request.AnnualMonth),
            StartMonth = frequency == Domain.Enums.RecurringFrequency.Monthly ? now.Month : 1,
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

        var frequency = (Domain.Enums.RecurringFrequency)request.Frequency;

        entity.AccountId = request.AccountId;
        entity.Type = request.Type;
        entity.Category = request.Type == Domain.Enums.TransactionType.Transfer ? Domain.Enums.TransactionCategory.Transfer : request.Category;
        entity.Amount = request.Amount;
        entity.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        entity.EntityType = request.Type == Domain.Enums.TransactionType.Transfer ? Domain.Enums.TransactionEntityType.Entity : request.EntityType;
        entity.EntityName = request.Type == Domain.Enums.TransactionType.Transfer || string.IsNullOrWhiteSpace(request.EntityName) ? null : request.EntityName.Trim();
        entity.DestinationAccountId = request.Type == Domain.Enums.TransactionType.Transfer ? request.DestinationAccountId : null;
        entity.ResponsibleUserId = await ResolveResponsibleUserAsync(request.ResponsibleUserId, request.Type, entity.HouseholdId, cancellationToken);
        entity.Frequency = frequency;
        entity.AnnualMonth = NormalizeAnnualMonth(frequency, request.AnnualMonth);
        // Non-monthly recorrentes contam desde o início do ano (igual ao comportamento das anuais);
        // ao mudar de mensal → não-mensal, ajusta o mês de início.
        if (frequency != Domain.Enums.RecurringFrequency.Monthly && entity.StartMonth != 1)
            entity.StartMonth = 1;
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

    /// <summary>
    /// Responsável válido = membro do agregado e fora de transferências. Caso contrário, null.
    /// </summary>
    private async Task<Guid?> ResolveResponsibleUserAsync(Guid? responsibleUserId, Domain.Enums.TransactionType type, Guid householdId, CancellationToken cancellationToken)
    {
        if (responsibleUserId is null || type == Domain.Enums.TransactionType.Transfer)
            return null;
        var members = await _userRepository.GetByHouseholdIdAsync(householdId, cancellationToken);
        return members.Any(m => m.Id == responsibleUserId.Value) ? responsibleUserId : null;
    }

    /// <summary>
    /// null para Mensal (não aplicável) e para o modo "diluir pelos 12 meses".
    /// Caso contrário, o mês de referência (1-12) onde o montante é lançado.
    /// </summary>
    private static int? NormalizeAnnualMonth(Domain.Enums.RecurringFrequency frequency, int? annualMonth)
    {
        if (frequency == Domain.Enums.RecurringFrequency.Monthly || annualMonth is null)
            return null;
        return Math.Clamp(annualMonth.Value, 1, 12);
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
            EntityType = r.EntityType,
            EntityName = r.EntityName,
            DestinationAccountId = r.DestinationAccountId,
            ResponsibleUserId = r.ResponsibleUserId,
            Frequency = (int)r.Frequency,
            AnnualMonth = r.AnnualMonth,
            StartMonth = r.StartMonth,
            StartYear = r.StartYear,
            EndMonth = r.EndMonth,
            EndYear = r.EndYear
        };
    }
}
