namespace Finora.Application.Options;

/// <summary>
/// Stripe billing config. Secrets and amounts come from env vars (Stripe__SecretKey, Stripe__ProMonthlyAmount, ...).
/// Amounts are in the currency's minor unit (cents). The API turns these into Stripe Products/Prices on demand,
/// so the front-end never sends a price — it only picks plan + interval.
/// </summary>
public class StripeOptions
{
    public const string SectionName = "Stripe";

    /// <summary>Secret API key (sk_...). Never expose to the client.</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Webhook signing secret (whsec_...) used to verify incoming events.</summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>ISO currency for all plans (lowercase, e.g. "eur").</summary>
    public string Currency { get; set; } = "eur";

    /// <summary>Pro plan price per month, in cents.</summary>
    public long ProMonthlyAmount { get; set; } = 799;

    /// <summary>Pro plan price per year, in cents (default = 10× monthly = 2 months free).</summary>
    public long ProAnnualAmount { get; set; } = 7990;

    /// <summary>Couple plan price per month, in cents.</summary>
    public long CoupleMonthlyAmount { get; set; } = 1299;

    /// <summary>Couple plan price per year, in cents (default = 10× monthly = 2 months free).</summary>
    public long CoupleAnnualAmount { get; set; } = 12990;
}
