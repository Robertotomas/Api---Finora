using Finora.Application.Options;
using Finora.Domain.Enums;
using Finora.Infrastructure.Services;

namespace Finora.Domain.Tests;

// Mapeamento puro (plano, intervalo) <-> Stripe lookup_key. O montante é parte da chave para que mudar
// o preço por env var crie um Price novo no Stripe (preços são imutáveis).
public class StripePriceCatalogTests
{
    private static StripeOptions Options() => new()
    {
        Currency = "eur",
        ProMonthlyAmount = 799,
        ProAnnualAmount = 7990,
        CoupleMonthlyAmount = 1299,
        CoupleAnnualAmount = 12990
    };

    [Theory]
    [InlineData(SubscriptionPlan.Pro, BillingInterval.Monthly, 799, "finora_pro_monthly_799_eur")]
    [InlineData(SubscriptionPlan.Pro, BillingInterval.Annual, 7990, "finora_pro_annual_7990_eur")]
    [InlineData(SubscriptionPlan.Couple, BillingInterval.Monthly, 1299, "finora_couple_monthly_1299_eur")]
    [InlineData(SubscriptionPlan.Couple, BillingInterval.Annual, 12990, "finora_couple_annual_12990_eur")]
    public void LookupKey_IsDeterministic_AndIncludesAmount(SubscriptionPlan plan, BillingInterval interval, long amount, string expected)
    {
        Assert.Equal(expected, StripePriceCatalog.LookupKey(plan, interval, amount, "eur"));
    }

    [Fact]
    public void LookupKey_FromOptions_UsesConfiguredAmounts()
    {
        var opts = Options();
        Assert.Equal("finora_pro_monthly_799_eur", StripePriceCatalog.LookupKey(opts, SubscriptionPlan.Pro, BillingInterval.Monthly));
        Assert.Equal("finora_couple_annual_12990_eur", StripePriceCatalog.LookupKey(opts, SubscriptionPlan.Couple, BillingInterval.Annual));
    }

    [Fact]
    public void ChangingAmount_ChangesLookupKey()
    {
        var a = StripePriceCatalog.LookupKey(SubscriptionPlan.Pro, BillingInterval.Monthly, 799, "eur");
        var b = StripePriceCatalog.LookupKey(SubscriptionPlan.Pro, BillingInterval.Monthly, 899, "eur");
        Assert.NotEqual(a, b);
    }

    [Theory]
    [InlineData(SubscriptionPlan.Pro, BillingInterval.Monthly)]
    [InlineData(SubscriptionPlan.Pro, BillingInterval.Annual)]
    [InlineData(SubscriptionPlan.Couple, BillingInterval.Monthly)]
    [InlineData(SubscriptionPlan.Couple, BillingInterval.Annual)]
    public void TryParse_RoundTrips(SubscriptionPlan plan, BillingInterval interval)
    {
        var key = StripePriceCatalog.LookupKey(Options(), plan, interval);

        Assert.True(StripePriceCatalog.TryParse(key, out var parsedPlan, out var parsedInterval));
        Assert.Equal(plan, parsedPlan);
        Assert.Equal(interval, parsedInterval);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("price_123")]                  // Stripe id, not ours
    [InlineData("finora_free_monthly_0_eur")]  // Free is not sold
    [InlineData("finora_pro_weekly_799_eur")]  // unknown interval
    [InlineData("finora_pro")]                 // too short
    public void TryParse_RejectsForeignKeys(string? key)
    {
        Assert.False(StripePriceCatalog.TryParse(key, out _, out _));
    }

    [Fact]
    public void AnnualDefault_IsTwoMonthsFree()
    {
        var opts = Options();
        // Annual price = 10× monthly (i.e. 2 months free).
        Assert.Equal(opts.ProMonthlyAmount * 10, opts.ProAnnualAmount);
        Assert.Equal(opts.CoupleMonthlyAmount * 10, opts.CoupleAnnualAmount);
    }

    [Theory]
    [InlineData(BillingInterval.Monthly, "month")]
    [InlineData(BillingInterval.Annual, "year")]
    public void StripeRecurringInterval_MapsCorrectly(BillingInterval interval, string expected)
    {
        Assert.Equal(expected, StripePriceCatalog.StripeRecurringInterval(interval));
    }
}
