using System.Collections.Concurrent;
using Finora.Application.Interfaces;
using Finora.Application.Options;
using Finora.Domain.Entities;
using Finora.Domain.Enums;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using StripeSubscription = Stripe.Subscription;

namespace Finora.Infrastructure.Services;

public class StripeService : IStripeService
{
    private readonly StripeOptions _options;
    private readonly AppOptions _appOptions;
    private readonly ApplicationDbContext _db;
    private readonly ISubscriptionService _subscriptionService;
    private readonly ILogger<StripeService> _logger;

    // Resolved Stripe ids are stable per process; cache to avoid hitting the API on every checkout.
    private static readonly ConcurrentDictionary<string, string> PriceCache = new();
    private static readonly ConcurrentDictionary<string, string> ProductCache = new();

    public StripeService(
        IOptions<StripeOptions> options,
        IOptions<AppOptions> appOptions,
        ApplicationDbContext db,
        ISubscriptionService subscriptionService,
        ILogger<StripeService> logger)
    {
        _options = options.Value;
        _appOptions = appOptions.Value;
        _db = db;
        _subscriptionService = subscriptionService;
        _logger = logger;

        if (!string.IsNullOrWhiteSpace(_options.SecretKey))
            StripeConfiguration.ApiKey = _options.SecretKey;
    }

    public async Task<string> CreateCheckoutUrlAsync(Guid householdId, SubscriptionPlan plan, BillingInterval interval, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var customerId = await EnsureCustomerAsync(householdId, cancellationToken);
        var priceId = await ResolvePriceIdAsync(plan, interval, cancellationToken);
        var appBase = _appOptions.PublicBaseUrl.TrimEnd('/');

        var options = new Stripe.Checkout.SessionCreateOptions
        {
            Mode = "subscription",
            Customer = customerId,
            ClientReferenceId = householdId.ToString(),
            AllowPromotionCodes = true,
            LineItems = new List<Stripe.Checkout.SessionLineItemOptions>
            {
                new() { Price = priceId, Quantity = 1 }
            },
            SubscriptionData = new Stripe.Checkout.SessionSubscriptionDataOptions
            {
                Metadata = new Dictionary<string, string> { ["householdId"] = householdId.ToString() }
            },
            Metadata = new Dictionary<string, string> { ["householdId"] = householdId.ToString() },
            SuccessUrl = $"{appBase}/subscription?checkout=success&session_id={{CHECKOUT_SESSION_ID}}",
            CancelUrl = $"{appBase}/subscription?checkout=cancel"
        };

        var session = await new Stripe.Checkout.SessionService().CreateAsync(options, null, cancellationToken);
        return session.Url;
    }

    public async Task<string> CreatePortalUrlAsync(Guid householdId, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var customerId = await EnsureCustomerAsync(householdId, cancellationToken);
        var appBase = _appOptions.PublicBaseUrl.TrimEnd('/');

        var session = await new Stripe.BillingPortal.SessionService().CreateAsync(
            new Stripe.BillingPortal.SessionCreateOptions
            {
                Customer = customerId,
                ReturnUrl = $"{appBase}/subscription"
            }, null, cancellationToken);

        return session.Url;
    }

    public async Task HandleWebhookAsync(string json, string signatureHeader, CancellationToken cancellationToken = default)
    {
        // Throws StripeException on a bad/forged signature — the controller turns that into a 400.
        var stripeEvent = EventUtility.ConstructEvent(
            json, signatureHeader, _options.WebhookSecret, throwOnApiVersionMismatch: false);

        switch (stripeEvent.Type)
        {
            case "customer.subscription.created":
            case "customer.subscription.updated":
            case "customer.subscription.deleted":
                if (stripeEvent.Data.Object is not StripeSubscription sub) break;
                await ApplySubscriptionStateAsync(sub, isDeleted: stripeEvent.Type == "customer.subscription.deleted", cancellationToken);
                break;

            default:
                _logger.LogDebug("Stripe webhook ignored event {Type}", stripeEvent.Type);
                break;
        }
    }

    public async Task SyncFromStripeAsync(Guid householdId, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var household = await _db.Households.FirstOrDefaultAsync(h => h.Id == householdId, cancellationToken);
        if (household?.StripeCustomerId is not { Length: > 0 } customerId)
            return; // no Stripe customer yet → nothing to reconcile

        var subs = await new Stripe.SubscriptionService().ListAsync(new SubscriptionListOptions
        {
            Customer = customerId,
            Status = "active",
            Limit = 1,
            Expand = new List<string> { "data.items.data.price" }
        }, null, cancellationToken);

        var active = subs.Data.FirstOrDefault();
        if (active == null)
        {
            await _subscriptionService.SyncStripeSubscriptionAsync(
                householdId, SubscriptionPlan.Free, SubscriptionStatus.Cancelled, null, cancellationToken);
            return;
        }

        var plan = await PlanForPriceAsync(active.Items?.Data?.FirstOrDefault()?.Price?.Id, cancellationToken);
        if (plan == null) return;

        await _subscriptionService.SyncStripeSubscriptionAsync(
            householdId, plan.Value, SubscriptionStatus.Active, active.Id, cancellationToken);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task ApplySubscriptionStateAsync(StripeSubscription sub, bool isDeleted, CancellationToken cancellationToken)
    {
        var householdId = await ResolveHouseholdIdAsync(sub, cancellationToken);
        if (householdId == null)
        {
            _logger.LogWarning("Stripe subscription {SubId} could not be matched to a household", sub.Id);
            return;
        }

        var inactive = isDeleted || sub.Status is "canceled" or "unpaid" or "incomplete_expired";
        if (inactive)
        {
            await _subscriptionService.SyncStripeSubscriptionAsync(
                householdId.Value, SubscriptionPlan.Free, SubscriptionStatus.Cancelled, null, cancellationToken);
            return;
        }

        if (sub.Status is "active" or "trialing" or "past_due")
        {
            var plan = await PlanForPriceAsync(sub.Items?.Data?.FirstOrDefault()?.Price?.Id, cancellationToken);
            if (plan == null)
            {
                _logger.LogWarning("Stripe subscription {SubId} has an unrecognised price", sub.Id);
                return;
            }

            await _subscriptionService.SyncStripeSubscriptionAsync(
                householdId.Value, plan.Value, SubscriptionStatus.Active, sub.Id, cancellationToken);
        }
    }

    private async Task<Guid?> ResolveHouseholdIdAsync(StripeSubscription sub, CancellationToken cancellationToken)
    {
        if (sub.Metadata != null
            && sub.Metadata.TryGetValue("householdId", out var raw)
            && Guid.TryParse(raw, out var fromMeta))
            return fromMeta;

        if (!string.IsNullOrEmpty(sub.CustomerId))
        {
            var household = await _db.Households
                .AsNoTracking()
                .FirstOrDefaultAsync(h => h.StripeCustomerId == sub.CustomerId, cancellationToken);
            if (household != null) return household.Id;
        }

        return null;
    }

    private async Task<string> EnsureCustomerAsync(Guid householdId, CancellationToken cancellationToken)
    {
        var household = await _db.Households.FirstOrDefaultAsync(h => h.Id == householdId, cancellationToken)
            ?? throw new InvalidOperationException("Agregado não encontrado.");

        if (household.StripeCustomerId is { Length: > 0 } existing)
            return existing;

        var user = await _db.Users
            .AsNoTracking()
            .Where(u => u.HouseholdId == householdId)
            .OrderBy(u => u.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var customer = await new CustomerService().CreateAsync(new CustomerCreateOptions
        {
            Email = user?.Email,
            Name = string.IsNullOrWhiteSpace(household.Name) ? user?.Email : household.Name,
            Metadata = new Dictionary<string, string> { ["householdId"] = householdId.ToString() }
        }, null, cancellationToken);

        household.StripeCustomerId = customer.Id;
        household.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return customer.Id;
    }

    private async Task<string> ResolvePriceIdAsync(SubscriptionPlan plan, BillingInterval interval, CancellationToken cancellationToken)
    {
        var lookupKey = StripePriceCatalog.LookupKey(_options, plan, interval);
        if (PriceCache.TryGetValue(lookupKey, out var cached))
            return cached;

        var existing = await new PriceService().ListAsync(new PriceListOptions
        {
            LookupKeys = new List<string> { lookupKey },
            Active = true,
            Limit = 1
        }, null, cancellationToken);

        var price = existing.Data.FirstOrDefault();
        if (price == null)
        {
            var productId = await EnsureProductAsync(plan, cancellationToken);
            price = await new PriceService().CreateAsync(new PriceCreateOptions
            {
                Product = productId,
                Currency = _options.Currency,
                UnitAmount = StripePriceCatalog.AmountFor(_options, plan, interval),
                Recurring = new PriceRecurringOptions
                {
                    Interval = StripePriceCatalog.StripeRecurringInterval(interval)
                },
                LookupKey = lookupKey,
                TransferLookupKey = true,
                Metadata = new Dictionary<string, string>
                {
                    ["finora_plan"] = StripePriceCatalog.PlanKey(plan),
                    ["finora_interval"] = StripePriceCatalog.IntervalKey(interval)
                }
            }, null, cancellationToken);
        }

        PriceCache[lookupKey] = price.Id;
        return price.Id;
    }

    private async Task<string> EnsureProductAsync(SubscriptionPlan plan, CancellationToken cancellationToken)
    {
        var planKey = StripePriceCatalog.PlanKey(plan);
        if (ProductCache.TryGetValue(planKey, out var cached))
            return cached;

        var search = await new ProductService().SearchAsync(new ProductSearchOptions
        {
            Query = $"active:'true' AND metadata['finora_plan']:'{planKey}'",
            Limit = 1
        }, null, cancellationToken);

        var product = search.Data.FirstOrDefault();
        if (product == null)
        {
            product = await new ProductService().CreateAsync(new ProductCreateOptions
            {
                Name = plan == SubscriptionPlan.Couple ? "Finora Couple" : "Finora Pro",
                Metadata = new Dictionary<string, string> { ["finora_plan"] = planKey }
            }, null, cancellationToken);
        }

        ProductCache[planKey] = product.Id;
        return product.Id;
    }

    private async Task<SubscriptionPlan?> PlanForPriceAsync(string? priceId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(priceId)) return null;

        var price = await new PriceService().GetAsync(priceId, null, null, cancellationToken);

        if (StripePriceCatalog.TryParse(price.LookupKey, out var plan, out _))
            return plan;

        if (price.Metadata != null && price.Metadata.TryGetValue("finora_plan", out var fromMeta))
        {
            if (fromMeta == "pro") return SubscriptionPlan.Pro;
            if (fromMeta == "couple") return SubscriptionPlan.Couple;
        }

        return null;
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.SecretKey))
            throw new InvalidOperationException("Stripe não está configurado (Stripe:SecretKey em falta).");
    }
}
