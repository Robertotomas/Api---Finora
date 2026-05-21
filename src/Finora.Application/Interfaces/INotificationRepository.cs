using Finora.Domain.Entities;

namespace Finora.Application.Interfaces;

public interface INotificationRepository
{
    Task<IReadOnlyList<Notification>> GetByHouseholdAsync(Guid householdId, Guid userId, int limit, int offset, CancellationToken cancellationToken = default);
    Task<int> GetUnreadCountAsync(Guid householdId, Guid userId, CancellationToken cancellationToken = default);
    Task MarkAsReadAsync(Guid id, Guid householdId, CancellationToken cancellationToken = default);
    Task MarkBatchAsReadAsync(IEnumerable<Guid> ids, Guid householdId, CancellationToken cancellationToken = default);
    Task MarkAllAsReadAsync(Guid householdId, Guid userId, CancellationToken cancellationToken = default);
    Task<bool> ExistsByDeduplicationKeyAsync(string key, CancellationToken cancellationToken = default);
    Task DeleteByDeduplicationKeyAsync(string key, CancellationToken cancellationToken = default);
    Task AddAsync(Notification notification, CancellationToken cancellationToken = default);
}
