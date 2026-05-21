using Finora.Application.Interfaces;
using Finora.Domain.Entities;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Finora.Infrastructure.Repositories;

public class NotificationRepository : INotificationRepository
{
    private readonly ApplicationDbContext _context;

    public NotificationRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<Notification>> GetByHouseholdAsync(Guid householdId, Guid userId, int limit, int offset, CancellationToken cancellationToken = default)
    {
        return await _context.Notifications
            .AsNoTracking()
            .Where(n => n.HouseholdId == householdId && !n.IsRead && (n.UserId == null || n.UserId == userId))
            .OrderByDescending(n => n.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> GetUnreadCountAsync(Guid householdId, Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.Notifications
            .AsNoTracking()
            .CountAsync(n => n.HouseholdId == householdId && !n.IsRead && (n.UserId == null || n.UserId == userId), cancellationToken);
    }

    public async Task MarkAsReadAsync(Guid id, Guid householdId, CancellationToken cancellationToken = default)
    {
        await _context.Notifications
            .Where(n => n.Id == id && n.HouseholdId == householdId && !n.IsRead)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.IsRead, true)
                .SetProperty(n => n.ReadAt, DateTime.UtcNow)
                .SetProperty(n => n.UpdatedAt, DateTime.UtcNow), cancellationToken);
    }

    public async Task MarkBatchAsReadAsync(IEnumerable<Guid> ids, Guid householdId, CancellationToken cancellationToken = default)
    {
        var idList = ids.ToList();
        if (idList.Count == 0) return;

        await _context.Notifications
            .Where(n => idList.Contains(n.Id) && n.HouseholdId == householdId && !n.IsRead)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.IsRead, true)
                .SetProperty(n => n.ReadAt, DateTime.UtcNow)
                .SetProperty(n => n.UpdatedAt, DateTime.UtcNow), cancellationToken);
    }

    public async Task MarkAllAsReadAsync(Guid householdId, Guid userId, CancellationToken cancellationToken = default)
    {
        await _context.Notifications
            .Where(n => n.HouseholdId == householdId && !n.IsRead && (n.UserId == null || n.UserId == userId))
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.IsRead, true)
                .SetProperty(n => n.ReadAt, DateTime.UtcNow)
                .SetProperty(n => n.UpdatedAt, DateTime.UtcNow), cancellationToken);
    }

    public async Task DeleteByDeduplicationKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        await _context.Notifications
            .Where(n => n.DeduplicationKey == key)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<bool> ExistsByDeduplicationKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        return await _context.Notifications
            .AsNoTracking()
            .AnyAsync(n => n.DeduplicationKey == key, cancellationToken);
    }

    public async Task AddAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        _context.Notifications.Add(notification);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
