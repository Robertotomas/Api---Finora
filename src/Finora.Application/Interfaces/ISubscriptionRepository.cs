using Finora.Domain.Entities;

namespace Finora.Application.Interfaces;

public interface ISubscriptionRepository
{
    Task<Subscription?> GetActiveByHouseholdIdAsync(Guid householdId, CancellationToken cancellationToken = default);
    Task<Subscription> CreateAsync(Subscription subscription, CancellationToken cancellationToken = default);
}

