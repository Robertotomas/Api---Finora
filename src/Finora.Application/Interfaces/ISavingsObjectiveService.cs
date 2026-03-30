using Finora.Application.DTOs.Objectives;

namespace Finora.Application.Interfaces;

public interface ISavingsObjectiveService
{
    Task<SavingsObjectivesOverviewDto> GetOverviewAsync(Guid householdId, Guid userId, CancellationToken cancellationToken = default);
    Task<SavingsObjectivesOverviewDto?> CreateAsync(CreateSavingsObjectiveRequest request, Guid householdId, Guid userId, CancellationToken cancellationToken = default);
    Task<SavingsObjectivesOverviewDto?> UpdateAsync(Guid objectiveId, UpdateSavingsObjectiveRequest request, Guid userId, CancellationToken cancellationToken = default);
    Task<SavingsObjectivesOverviewDto?> FinalizeAsync(Guid objectiveId, Guid userId, CancellationToken cancellationToken = default);
    Task<SavingsObjectivesOverviewDto?> DeleteAsync(Guid objectiveId, Guid userId, CancellationToken cancellationToken = default);
}
