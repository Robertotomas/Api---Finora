using Finora.Domain.Enums;

namespace Finora.Application.Interfaces;

public interface ISubscriptionService
{
    Task<SubscriptionPlan?> GetActivePlanAsync(Guid householdId, CancellationToken cancellationToken = default);
    Task<bool> CanAddAccountAsync(Guid householdId, CancellationToken cancellationToken = default);
    Task<bool> CanAddTransactionAsync(Guid householdId, TransactionType type, int year, int month, CancellationToken cancellationToken = default);
    Task<bool> CanAccessObjectivesAsync(Guid householdId, CancellationToken cancellationToken = default);
    Task<bool> CanAccessMonthlyReportsAsync(Guid householdId, CancellationToken cancellationToken = default);
    Task<bool> CanAccessRecurringAsync(Guid householdId, CancellationToken cancellationToken = default);
    Task<bool> CanAccessAssetsAsync(Guid householdId, CancellationToken cancellationToken = default);
    Task<bool> CanAccessInvestmentsAsync(Guid householdId, CancellationToken cancellationToken = default);
    Task<DateTime?> GetPaidPlanStartDateAsync(Guid householdId, CancellationToken cancellationToken = default);
    Task UpgradeAsync(Guid householdId, SubscriptionPlan plan, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reconcile the local active subscription with Stripe's truth (called from the webhook and the post-checkout sync).
    /// Idempotent: re-applying the same Stripe state is a no-op. A non-active status (or Free) cancels the local row.
    /// </summary>
    Task SyncStripeSubscriptionAsync(
        Guid householdId,
        SubscriptionPlan plan,
        SubscriptionStatus status,
        string? stripeSubscriptionId,
        CancellationToken cancellationToken = default);

    /// <summary>Free plan with 2+ accounts: whether a primary must be chosen, and the current primary if any.</summary>
    Task<(bool FreeMultiAccount, bool NeedsPrimarySelection, Guid? PrimaryAccountId)> GetFreeMultiAccountStateAsync(
        Guid householdId,
        CancellationToken cancellationToken = default);

    /// <summary>Whether transactions/recurrings/edits may target this account (Free multi-account rules).</summary>
    Task<bool> CanUseAccountForActivityAsync(Guid householdId, Guid accountId, CancellationToken cancellationToken = default);
}

