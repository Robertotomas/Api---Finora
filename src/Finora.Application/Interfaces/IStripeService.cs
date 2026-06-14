using Finora.Domain.Enums;

namespace Finora.Application.Interfaces;

/// <summary>
/// Server-side Stripe billing. The client only ever picks (plan, interval); the price is resolved here from
/// configured amounts, so it can never be tampered with from the browser.
/// </summary>
public interface IStripeService
{
    /// <summary>Create a hosted Checkout session for a paid plan and return its URL (front-end redirects to it).</summary>
    Task<string> CreateCheckoutUrlAsync(Guid householdId, SubscriptionPlan plan, BillingInterval interval, CancellationToken cancellationToken = default);

    /// <summary>Create a Customer Portal session and return its URL (manage/cancel subscription).</summary>
    Task<string> CreatePortalUrlAsync(Guid householdId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verify and process a raw webhook payload. Throws <see cref="Stripe.StripeException"/> on an invalid
    /// signature so the controller can answer 400. Plan changes are applied via the subscription service.
    /// </summary>
    Task HandleWebhookAsync(string json, string signatureHeader, CancellationToken cancellationToken = default);

    /// <summary>
    /// Read the household's current subscription straight from Stripe and reconcile the local plan with it.
    /// Authoritative — used when the user returns from Checkout (does not rely on the webhook).
    /// </summary>
    Task SyncFromStripeAsync(Guid householdId, CancellationToken cancellationToken = default);
}
