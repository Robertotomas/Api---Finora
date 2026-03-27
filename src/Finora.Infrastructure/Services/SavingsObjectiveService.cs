using Finora.Application.DTOs.Objectives;
using Finora.Application.Interfaces;
using Finora.Domain.Entities;

namespace Finora.Infrastructure.Services;

public class SavingsObjectiveService : ISavingsObjectiveService
{
    private readonly ISavingsObjectiveRepository _objectivesRepository;
    private readonly IDashboardRepository _dashboardRepository;
    private readonly IUserRepository _userRepository;
    private readonly ISubscriptionService _subscriptionService;

    public SavingsObjectiveService(
        ISavingsObjectiveRepository objectivesRepository,
        IDashboardRepository dashboardRepository,
        IUserRepository userRepository,
        ISubscriptionService subscriptionService)
    {
        _objectivesRepository = objectivesRepository;
        _dashboardRepository = dashboardRepository;
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

    private async Task<SavingsObjectivesOverviewDto> BuildOverviewAsync(
        Guid householdId,
        IReadOnlyList<SavingsObjective> objectives,
        CancellationToken cancellationToken)
    {
        var totalIncome = await _dashboardRepository.GetTotalIncomeThroughLastClosedMonthAsync(householdId, cancellationToken);
        var totalExpenses = await _dashboardRepository.GetTotalExpensesThroughLastClosedMonthAsync(householdId, cancellationToken);
        var totalSavings = Math.Max(0m, totalIncome - totalExpenses);

        var completed = objectives
            .Where(x => x.CompletedAt.HasValue)
            .OrderByDescending(x => x.CompletedAt)
            .ToList();
        var reservedByCompleted = completed.Sum(x => x.TargetAmount);
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
            CompletedAt = x.CompletedAt!.Value
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

    private async Task<Dictionary<Guid, decimal>> BuildActiveAllocationsAsync(
        Guid householdId,
        IReadOnlyList<SavingsObjective> objectives,
        CancellationToken cancellationToken)
    {
        var totalIncome = await _dashboardRepository.GetTotalIncomeThroughLastClosedMonthAsync(householdId, cancellationToken);
        var totalExpenses = await _dashboardRepository.GetTotalExpensesThroughLastClosedMonthAsync(householdId, cancellationToken);
        var totalSavings = Math.Max(0m, totalIncome - totalExpenses);
        var reservedByCompleted = objectives.Where(x => x.CompletedAt.HasValue).Sum(x => x.TargetAmount);
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
