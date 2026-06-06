using Finora.Application.DTOs.Objectives;
using Finora.Application.Interfaces;
using Finora.Domain.Entities;

namespace Finora.Infrastructure.Services;

public class SavingsObjectiveService : ISavingsObjectiveService
{
    private readonly ISavingsObjectiveRepository _objectivesRepository;
    private readonly IDashboardRepository _dashboardRepository;
    private readonly IRecurringTransactionRepository _recurringRepository;
    private readonly IUserRepository _userRepository;
    private readonly ISubscriptionService _subscriptionService;

    public SavingsObjectiveService(
        ISavingsObjectiveRepository objectivesRepository,
        IDashboardRepository dashboardRepository,
        IRecurringTransactionRepository recurringRepository,
        IUserRepository userRepository,
        ISubscriptionService subscriptionService)
    {
        _objectivesRepository = objectivesRepository;
        _dashboardRepository = dashboardRepository;
        _recurringRepository = recurringRepository;
        _userRepository = userRepository;
        _subscriptionService = subscriptionService;
    }

    public async Task<SavingsObjectivesOverviewDto> GetOverviewAsync(Guid householdId, Guid userId, CancellationToken cancellationToken = default)
    {
        if (!await UserBelongsToHouseholdAsync(userId, householdId, cancellationToken))
        {
            return new SavingsObjectivesOverviewDto();
        }

        var objectives = await _objectivesRepository.GetByHouseholdAsync(householdId, cancellationToken);
        return await BuildOverviewAsync(householdId, objectives, cancellationToken);
    }

    public async Task<SavingsObjectivesOverviewDto?> CreateAsync(
        CreateSavingsObjectiveRequest request,
        Guid householdId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (!await UserBelongsToHouseholdAsync(userId, householdId, cancellationToken))
            return null;

        if (!await _subscriptionService.CanAccessObjectivesAsync(householdId, cancellationToken))
            return null;

        var maxSortOrder = await _objectivesRepository.GetMaxSortOrderAsync(householdId, cancellationToken);
        var objective = new SavingsObjective
        {
            Id = Guid.NewGuid(),
            HouseholdId = householdId,
            Name = request.Name.Trim(),
            TargetAmount = request.TargetAmount,
            TargetDate = request.TargetDate,
            SortOrder = maxSortOrder + 1,
            CreatedAt = DateTime.UtcNow
        };

        await _objectivesRepository.CreateAsync(objective, cancellationToken);
        var all = await _objectivesRepository.GetByHouseholdAsync(householdId, cancellationToken);
        return await BuildOverviewAsync(householdId, all, cancellationToken);
    }

    public async Task<SavingsObjectivesOverviewDto?> UpdateAsync(
        Guid objectiveId,
        UpdateSavingsObjectiveRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var objective = await _objectivesRepository.GetByIdAsync(objectiveId, cancellationToken);
        if (objective == null)
            return null;
        if (!await UserBelongsToHouseholdAsync(userId, objective.HouseholdId, cancellationToken))
            return null;
        if (!await _subscriptionService.CanAccessObjectivesAsync(objective.HouseholdId, cancellationToken))
            return null;
        if (objective.CompletedAt.HasValue)
            return null;

        objective.Name = request.Name.Trim();
        objective.TargetAmount = request.TargetAmount;
        objective.TargetDate = request.TargetDate;
        objective.UpdatedAt = DateTime.UtcNow;

        await _objectivesRepository.UpdateAsync(objective, cancellationToken);
        var all = await _objectivesRepository.GetByHouseholdAsync(objective.HouseholdId, cancellationToken);
        return await BuildOverviewAsync(objective.HouseholdId, all, cancellationToken);
    }

    public async Task<SavingsObjectivesOverviewDto?> FinalizeAsync(
        Guid objectiveId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var objective = await _objectivesRepository.GetByIdAsync(objectiveId, cancellationToken);
        if (objective == null)
            return null;
        if (!await UserBelongsToHouseholdAsync(userId, objective.HouseholdId, cancellationToken))
            return null;
        if (!await _subscriptionService.CanAccessObjectivesAsync(objective.HouseholdId, cancellationToken))
            return null;
        if (objective.CompletedAt.HasValue)
            return null;

        var beforeFinalize = await _objectivesRepository.GetByHouseholdAsync(objective.HouseholdId, cancellationToken);
        var active = beforeFinalize
            .Where(x => !x.CompletedAt.HasValue)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.CreatedAt)
            .ToList();
        var target = active.FirstOrDefault(x => x.Id == objective.Id);
        if (target == null)
            return null;

        var allocations = await BuildActiveAllocationsAsync(objective.HouseholdId, beforeFinalize, cancellationToken);
        var canFinalize = allocations.TryGetValue(objective.Id, out var allocated) && allocated >= objective.TargetAmount;
        if (!canFinalize)
            return null;

        objective.CompletedAt = DateTime.UtcNow;
        objective.UpdatedAt = DateTime.UtcNow;
        await _objectivesRepository.UpdateAsync(objective, cancellationToken);

        var all = await _objectivesRepository.GetByHouseholdAsync(objective.HouseholdId, cancellationToken);
        return await BuildOverviewAsync(objective.HouseholdId, all, cancellationToken);
    }

    public async Task<SavingsObjectivesOverviewDto?> LiquidateAsync(
        Guid objectiveId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var objective = await _objectivesRepository.GetByIdAsync(objectiveId, cancellationToken);
        if (objective == null)
            return null;
        if (!await UserBelongsToHouseholdAsync(userId, objective.HouseholdId, cancellationToken))
            return null;
        if (!await _subscriptionService.CanAccessObjectivesAsync(objective.HouseholdId, cancellationToken))
            return null;
        // Só objetivos concluídos e ainda não liquidados podem ser liquidados.
        if (!objective.CompletedAt.HasValue || objective.LiquidatedAt.HasValue)
            return null;

        objective.LiquidatedAt = DateTime.UtcNow;
        objective.UpdatedAt = DateTime.UtcNow;
        await _objectivesRepository.UpdateAsync(objective, cancellationToken);

        var all = await _objectivesRepository.GetByHouseholdAsync(objective.HouseholdId, cancellationToken);
        return await BuildOverviewAsync(objective.HouseholdId, all, cancellationToken);
    }

    public async Task<SavingsObjectivesOverviewDto?> DeleteAsync(
        Guid objectiveId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var objective = await _objectivesRepository.GetByIdAsync(objectiveId, cancellationToken);
        if (objective == null)
            return null;
        if (!await UserBelongsToHouseholdAsync(userId, objective.HouseholdId, cancellationToken))
            return null;
        if (!await _subscriptionService.CanAccessObjectivesAsync(objective.HouseholdId, cancellationToken))
            return null;

        var householdId = objective.HouseholdId;
        var deleted = await _objectivesRepository.DeleteAsync(objectiveId, cancellationToken);
        if (!deleted)
            return null;

        var all = await _objectivesRepository.GetByHouseholdAsync(householdId, cancellationToken);
        return await BuildOverviewAsync(householdId, all, cancellationToken);
    }

    private async Task<SavingsObjectivesOverviewDto> BuildOverviewAsync(
        Guid householdId,
        IReadOnlyList<SavingsObjective> objectives,
        CancellationToken cancellationToken)
    {
        var totalSavings = await ComputeTotalSavingsThroughLastClosedMonthAsync(householdId, cancellationToken);

        var completed = objectives
            .Where(x => x.CompletedAt.HasValue)
            .OrderByDescending(x => x.CompletedAt)
            .ToList();
        // "Reservado" = objetivos concluídos ainda NÃO liquidados: o dinheiro está
        // apartado para esse objetivo e não pode ser usado por outros, mas ainda não
        // foi gasto. Os liquidados JÁ foram gastos através de uma despesa real (o fluxo
        // de liquidação obriga a registar a despesa), por isso já baixaram a poupança
        // acumulada — NÃO os voltamos a subtrair aqui, senão estaríamos a contar o
        // gasto a dobrar.
        var reservedByCompleted = completed.Where(x => !x.LiquidatedAt.HasValue).Sum(x => x.TargetAmount);
        var availablePool = Math.Max(0m, totalSavings - reservedByCompleted);

        var active = objectives
            .Where(x => !x.CompletedAt.HasValue)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.CreatedAt)
            .ToList();

        var activeDtos = new List<SavingsObjectiveActiveDto>(active.Count);
        foreach (var item in active)
        {
            var allocated = Math.Min(availablePool, item.TargetAmount);
            var progressPercent = item.TargetAmount <= 0
                ? 0
                : Math.Round((allocated / item.TargetAmount) * 100m, 2);

            activeDtos.Add(new SavingsObjectiveActiveDto
            {
                Id = item.Id,
                Name = item.Name,
                TargetAmount = item.TargetAmount,
                TargetDate = item.TargetDate,
                SortOrder = item.SortOrder,
                AllocatedAmount = allocated,
                ProgressPercent = progressPercent,
                CanFinalize = allocated >= item.TargetAmount
            });
        }

        var historyDtos = completed.Select(x => new SavingsObjectiveHistoryDto
        {
            Id = x.Id,
            Name = x.Name,
            TargetAmount = x.TargetAmount,
            TargetDate = x.TargetDate,
            SortOrder = x.SortOrder,
            CompletedAt = x.CompletedAt!.Value,
            LiquidatedAt = x.LiquidatedAt
        }).ToList();

        return new SavingsObjectivesOverviewDto
        {
            TotalSavings = totalSavings,
            ReservedByCompletedObjectives = reservedByCompleted,
            AvailableForActiveObjectives = availablePool,
            ActiveObjectives = activeDtos,
            HistoryObjectives = historyDtos
        };
    }

    /// <summary>
    /// Poupança acumulada até ao último mês fechado (mês anterior ao atual). Soma os movimentos
    /// reais e também os recorrentes nos meses em que estavam ativos — tal como o dashboard e os
    /// relatórios — para que receitas/despesas recorrentes contem na poupança dos meses em que
    /// existiram (e deixem de contar a partir do mês em que foram removidos).
    /// <para>
    /// Pode ser <b>negativo</b> (défice acumulado): o valor real é exposto em
    /// <c>TotalSavings</c> para o utilizador perceber o défice; o clamp a ≥ 0 é aplicado apenas
    /// ao "disponível para ativos" (não se pode reservar dinheiro negativo para objetivos).
    /// </para>
    /// </summary>
    private async Task<decimal> ComputeTotalSavingsThroughLastClosedMonthAsync(Guid householdId, CancellationToken cancellationToken)
    {
        var income = await _dashboardRepository.GetTotalIncomeThroughLastClosedMonthAsync(householdId, cancellationToken);
        var expenses = await _dashboardRepository.GetTotalExpensesThroughLastClosedMonthAsync(householdId, cancellationToken);

        // Último mês fechado = mês anterior ao corrente (mesma fronteira dos movimentos reais).
        var lastClosed = DateTime.UtcNow.AddMonths(-1);
        var minStart = await _recurringRepository.GetMinimumStartMonthAsync(householdId, cancellationToken);
        if (minStart is { } ms)
        {
            // GetAggregatedForMonthRangeAsync já protege quando o range é vazio (start > end).
            var agg = await _recurringRepository.GetAggregatedForMonthRangeAsync(
                householdId, ms.Year, ms.Month, lastClosed.Year, lastClosed.Month, cancellationToken);
            income += agg.TotalIncome;
            expenses += agg.TotalExpenses;
        }

        return income - expenses;
    }

    private async Task<Dictionary<Guid, decimal>> BuildActiveAllocationsAsync(
        Guid householdId,
        IReadOnlyList<SavingsObjective> objectives,
        CancellationToken cancellationToken)
    {
        var totalSavings = await ComputeTotalSavingsThroughLastClosedMonthAsync(householdId, cancellationToken);
        // Só os concluídos NÃO liquidados reservam dinheiro. Os liquidados já foram
        // gastos via despesa real (já refletida na poupança) — ver BuildOverviewAsync.
        var reservedByCompleted = objectives
            .Where(x => x.CompletedAt.HasValue && !x.LiquidatedAt.HasValue)
            .Sum(x => x.TargetAmount);
        var pool = Math.Max(0m, totalSavings - reservedByCompleted);

        var active = objectives
            .Where(x => !x.CompletedAt.HasValue)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.CreatedAt)
            .ToList();

        var allocations = new Dictionary<Guid, decimal>();
        foreach (var item in active)
            allocations[item.Id] = Math.Min(pool, item.TargetAmount);
        return allocations;
    }

    private async Task<bool> UserBelongsToHouseholdAsync(Guid userId, Guid householdId, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        return user != null && user.HouseholdId.HasValue && user.HouseholdId.Value == householdId;
    }
}
