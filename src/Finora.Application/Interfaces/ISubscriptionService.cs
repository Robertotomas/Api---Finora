using Finora.Domain.Enums;

namespace Finora.Application.Interfaces;

public interface ISubscriptionService
{
    Task<SubscriptionPlan?> GetActivePlanAsync(Guid householdId, CancellationToken cancellationToken = default);
    Task<bool> CanAddAccountAsync(Guid householdId, CancellationToken cancellationToken = default);
    Task<bool> CanAddTransactionAsync(Guid householdId, TransactionType type, int year, int month, CancellationToken cancellationToken = default);
    Task<bool> CanAccessObjectivesAsync(Guid householdId, CancellationToken cancellationToken = default);
    Task UpgradeAsync(Guid householdId, SubscriptionPlan plan, CancellationToken cancellationToken = default);
}

