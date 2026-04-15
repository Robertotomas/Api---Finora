using Finora.Domain.Entities;

namespace Finora.Application.Interfaces;

public interface IHouseholdRepository
{
    Task<Household?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Household?> GetByIdWithUsersAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Household?> GetByIdTrackedAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Household> CreateAsync(Household household, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>All household ids (for background jobs).</summary>
    Task<IReadOnlyList<Guid>> GetAllHouseholdIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>Removes a household (e.g. empty orphan after user moved to another household).</summary>
    Task DeleteAsync(Household household, CancellationToken cancellationToken = default);
}
