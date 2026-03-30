using Finora.Domain.Entities;

namespace Finora.Application.Interfaces;

public interface ISavingsObjectiveRepository
{
    Task<IReadOnlyList<SavingsObjective>> GetByHouseholdAsync(Guid householdId, CancellationToken cancellationToken = default);
    Task<SavingsObjective?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<int> GetMaxSortOrderAsync(Guid householdId, CancellationToken cancellationToken = default);
    Task<SavingsObjective> CreateAsync(SavingsObjective objective, CancellationToken cancellationToken = default);
    Task<SavingsObjective> UpdateAsync(SavingsObjective objective, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
