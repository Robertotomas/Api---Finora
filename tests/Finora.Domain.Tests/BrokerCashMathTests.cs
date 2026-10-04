using Finora.Domain.Entities;
using Finora.Domain.Enums;
using Finora.Infrastructure.Services;

namespace Finora.Domain.Tests;

// Dinheiro parado na corretora = depósitos − compras + vendas (≥ 0). Conta para o Património Total.
public class BrokerCashMathTests
{
    private static InvestmentTransaction Tx(InvestmentOperation op, decimal qty, decimal price,
        decimal commission = 0m, decimal fx = 1m, decimal fxFee = 0m) => new()
    {
        Operation = op, Quantity = qty, UnitPrice = price, Commission = commission,
        FxRateToEur = fx, FxFeePercent = fxFee, Date = DateTime.UtcNow,
    };

    [Fact]
    public void NoDeposits_IsZero_EvenWithSales()
    {
        var txs = new[] { Tx(InvestmentOperation.Sell, 10, 50) };
        Assert.Equal(0m, BrokerCashMath.UninvestedCashEur(false, 0m, txs));
    }

    [Fact]
    public void DepositWithoutPurchases_IsAllCash()
    {
        Assert.Equal(300m, BrokerCashMath.UninvestedCashEur(true, 300m, Array.Empty<InvestmentTransaction>()));
    }

    [Fact]
    public void Purchase_ConsumesCash_IncludingCommission()
    {
        // 300 depositados; compra 2 × 100 € + 1 € comissão → sobram 99.
        var txs = new[] { Tx(InvestmentOperation.Buy, 2, 100, commission: 1) };
        Assert.Equal(99m, BrokerCashMath.UninvestedCashEur(true, 300m, txs));
    }

    [Fact]
    public void ForeignPurchase_UsesFxRateAndFee()
    {
        // 1 × 100 USD a 0,9 €/USD com fee de câmbio 0,5% → 90,45 €.
        var txs = new[] { Tx(InvestmentOperation.Buy, 1, 100, fx: 0.9m, fxFee: 0.5m) };
        Assert.Equal(209.55m, BrokerCashMath.UninvestedCashEur(true, 300m, txs));
    }

    [Fact]
    public void Sale_ReturnsNetProceeds()
    {
        // 300 − 200 (compra) + (2 × 120 − 2 comissão) = 338.
        var txs = new[]
        {
            Tx(InvestmentOperation.Buy, 2, 100),
            Tx(InvestmentOperation.Sell, 2, 120, commission: 2),
        };
        Assert.Equal(338m, BrokerCashMath.UninvestedCashEur(true, 300m, txs));
    }

    [Fact]
    public void PurchasesBeyondDeposits_ClampToZero()
    {
        // Compras anteriores aos depósitos registados → não inventa saldo negativo.
        var txs = new[] { Tx(InvestmentOperation.Buy, 10, 100) };
        Assert.Equal(0m, BrokerCashMath.UninvestedCashEur(true, 300m, txs));
    }
}
