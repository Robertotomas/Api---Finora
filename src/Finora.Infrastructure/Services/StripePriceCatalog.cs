using Finora.Application.Options;
using Finora.Domain.Enums;

namespace Finora.Infrastructure.Services;

/// <summary>
/// Pure mapping between (plan, interval) and the Stripe Price identity. The amount is baked into the
/// <c>lookup_key</c> so that changing a price via env var produces a NEW Stripe Price automatically
/// (Stripe prices are immutable); existing subscriptions keep their old price. No external calls here —
/// kept side-effect free so it can be unit tested.
/// </summary>
public static class StripePriceCatalog
{
    /// <summary>Stable Stripe product identity per plan (matched via product metadata <c>finora_plan</c>).</summary>
    public static string PlanKey(SubscriptionPlan plan) => plan switch
    {
        SubscriptionPlan.Pro => "pro",
        SubscriptionPlan.Couple => "couple",
        _ => throw new ArgumentOutOfRangeException(nameof(plan), plan, "Only paid plans are sold via Stripe.")
    };

    public static string IntervalKey(BillingInterval interval) => interval switch
    {
        BillingInterval.Monthly => "monthly",
        BillingInterval.Annual => "annual",
        _ => throw new ArgumentOutOfRangeException(nameof(interval), interval, null)
    };

    /// <summary>Stripe recurring interval ("month"/"year").</summary>
    public static string StripeRecurringInterval(BillingInterval interval) => interval switch
    {
        BillingInterval.Monthly => "month",
        BillingInterval.Annual => "year",
        _ => throw new ArgumentOutOfRangeException(nameof(interval), interval, null)
    };

    /// <summary>Amount (cents) for a plan/interval pair, read from configured options.</summary>
    public static long AmountFor(StripeOptions options, SubscriptionPlan plan, BillingInterval interval) =>
        (plan, interval) switch
        {
            (SubscriptionPlan.Pro, BillingInterval.Monthly) => options.ProMonthlyAmount,
            (SubscriptionPlan.Pro, BillingInterval.Annual) => options.ProAnnualAmount,
            (SubscriptionPlan.Couple, BillingInterval.Monthly) => options.CoupleMonthlyAmount,
            (SubscriptionPlan.Couple, BillingInterval.Annual) => options.CoupleAnnualAmount,
            _ => throw new ArgumentOutOfRangeException(nameof(plan), plan, "Only paid plans are sold via Stripe.")
        };

    /// <summary>
    /// Deterministic lookup key, e.g. <c>finora_pro_monthly_799_eur</c>. Changing the amount changes the key,
    /// so a new Price is created on next checkout instead of silently mismatching.
    /// </summary>
    public static string LookupKey(SubscriptionPlan plan, BillingInterval interval, long amount, string currency) =>
        $"finora_{PlanKey(plan)}_{IntervalKey(interval)}_{amount}_{currency.ToLowerInvariant()}";

    public static string LookupKey(StripeOptions options, SubscriptionPlan plan, BillingInterval interval) =>
        LookupKey(plan, interval, AmountFor(options, plan, interval), options.Currency);

    /// <summary>Parse the plan + interval back out of a Finora lookup key. Returns false for anything else.</summary>
    public static bool TryParse(string? lookupKey, out SubscriptionPlan plan, out BillingInterval interval)
    {
        plan = SubscriptionPlan.Free;
        interval = BillingInterval.Monthly;

        if (string.IsNullOrWhiteSpace(lookupKey)) return false;

        var parts = lookupKey.Split('_');
        // finora_{plan}_{interval}_{amount}_{currency}
        if (parts.Length < 5 || parts[0] != "finora") return false;

        plan = parts[1] switch
        {
            "pro" => SubscriptionPlan.Pro,
            "couple" => SubscriptionPlan.Couple,
            _ => SubscriptionPlan.Free
        };
        if (plan == SubscriptionPlan.Free) return false;

        switch (parts[2])
        {
            case "monthly": interval = BillingInterval.Monthly; break;
            case "annual": interval = BillingInterval.Annual; break;
            default: return false;
        }

        return true;
    }
}
