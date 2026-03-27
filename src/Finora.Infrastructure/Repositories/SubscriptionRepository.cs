using Finora.Application.Interfaces;
using Finora.Domain.Enums;
using Finora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Finora.Infrastructure.Persistence;

namespace Finora.Infrastructure.Repositories;

public class SubscriptionRepository : ISubscriptionRepository
{
    private readonly ApplicationDbContext _context;

    public SubscriptionRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Subscription?> GetActiveByHouseholdIdAsync(Guid householdId, CancellationToken cancellationToken = default)
    {
        return await _context.Subscriptions
            .Where(s => s.HouseholdId == householdId && s.Status == SubscriptionStatus.Active)
            .OrderByDescending(s => s.StartedAt)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<Subscription> CreateAsync(Subscription subscription, CancellationToken cancellationToken = default)
    {
        _context.Subscriptions.Add(subscription);
        await _context.SaveChangesAsync(cancellationToken);
        return subscription;
    }
}

