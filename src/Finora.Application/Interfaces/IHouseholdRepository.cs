using Finora.Domain.Entities;

namespace Finora.Application.Interfaces;

public interface IHouseholdRepository
{
    Task<Household?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Household?> GetByIdWithUsersAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Household?> GetByIdTrackedAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Household> CreateAsync(Household household, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
