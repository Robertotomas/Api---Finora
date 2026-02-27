using Finora.Application.DTOs.Household;

namespace Finora.Application.Interfaces;

public interface IHouseholdService
{
    Task<HouseholdDto?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HouseholdMemberDto>> GetMembersAsync(Guid householdId, Guid userId, CancellationToken cancellationToken = default);
    Task<HouseholdDto?> GetOrCreateForUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<HouseholdDto?> UpdateAsync(Guid id, UpdateHouseholdRequest request, Guid userId, CancellationToken cancellationToken = default);
}
