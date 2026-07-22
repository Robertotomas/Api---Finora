using Finora.Application.Interfaces;
using Finora.Infrastructure.Services;

namespace Finora.Domain.Tests;

// Ajuste de quantidades a stock splits. Os preços do Yahoo já vêm ajustados a splits, por isso a
// quantidade comprada antes de um split tem de ser multiplicada pelo fator para o valor bater certo.
public class SplitMathTests
{
    private static DateOnly D(string s) => DateOnly.Parse(s);

    [Fact]
    public void NoSplits_FactorIsOne()
    {
        Assert.Equal(1m, SplitMath.FactorAfter(new List<StockSplit>(), D("2020-01-01")));
    }

    [Fact]
    public void SplitAfterPurchase_MultipliesQuantity()
    {
        var splits = new List<StockSplit> { new(D("2020-08-31"), 4m) }; // Apple 4:1
        // Comprou antes do split → 1 ação passa a valer 4 na escala atual.
        Assert.Equal(4m, SplitMath.FactorAfter(splits, D("2020-01-01")));
    }

    [Fact]
    public void SplitBeforePurchase_DoesNotAffect()
    {
        var splits = new List<StockSplit> { new(D("2020-08-31"), 4m) };
        // Comprou depois do split → já detém na escala atual, fator 1.
        Assert.Equal(1m, SplitMath.FactorAfter(splits, D("2021-01-01")));
    }

    [Fact]
    public void PurchaseOnSplitDay_IsPostSplit()
    {
        var splits = new List<StockSplit> { new(D("2020-08-31"), 4m) };
        // Regra estrita "depois de": comprar no dia do split conta como pós-split (fator 1).
        Assert.Equal(1m, SplitMath.FactorAfter(splits, D("2020-08-31")));
    }

    [Fact]
    public void MultipleSplits_AccumulateOnlyThoseAfter()
    {
        // Apple: 7:1 (2014) e 4:1 (2020). Compra em 2016 apanha só o de 2020.
        var splits = new List<StockSplit>
        {
            new(D("2014-06-09"), 7m),
            new(D("2020-08-31"), 4m),
        };
        Assert.Equal(4m, SplitMath.FactorAfter(splits, D("2016-01-01")));   // só o de 2020
        Assert.Equal(28m, SplitMath.FactorAfter(splits, D("2010-01-01")));  // 7 × 4
        Assert.Equal(1m, SplitMath.FactorAfter(splits, D("2021-01-01")));   // nenhum depois
    }

    [Fact]
    public void FractionalRatio_IsSupported()
    {
        var splits = new List<StockSplit> { new(D("2007-09-11"), 1.5m) }; // NVDA 3:2
        Assert.Equal(1.5m, SplitMath.FactorAfter(splits, D("2007-01-01")));
    }

    [Fact]
    public void ReverseSplit_ReducesQuantity()
    {
        // Reverse split 1:8 (GE, 2021): rácio 0.125 → quem detinha 8 ações passa a 1.
        var splits = new List<StockSplit> { new(D("2021-08-02"), 0.125m) };
        Assert.Equal(0.125m, SplitMath.FactorAfter(splits, D("2020-01-01"))); // comprou antes → × 0.125
        Assert.Equal(1m, SplitMath.FactorAfter(splits, D("2022-01-01")));     // comprou depois → 1
    }

    [Fact]
    public void MixedForwardAndReverseSplits_Accumulate()
    {
        // Split 2:1 seguido de reverse 1:4: fator = 2 × 0.25 = 0.5 para quem comprou antes de ambos.
        var splits = new List<StockSplit>
        {
            new(D("2018-01-01"), 2m),
            new(D("2022-01-01"), 0.25m),
        };
        Assert.Equal(0.5m, SplitMath.FactorAfter(splits, D("2017-01-01")));
        Assert.Equal(0.25m, SplitMath.FactorAfter(splits, D("2019-01-01"))); // só o reverse
    }

    [Fact]
    public void IgnoresNonPositiveRatios()
    {
        var splits = new List<StockSplit> { new(D("2020-01-01"), 0m), new(D("2021-01-01"), 2m) };
        Assert.Equal(2m, SplitMath.FactorAfter(splits, D("2019-01-01")));
    }

    // ── AdjustQuantity: aplica o fator e trunca frações em reverse splits (cash-in-lieu) ──

    [Fact]
    public void AdjustQuantity_NoSplits_ReturnsOriginal()
    {
        Assert.Equal(10m, SplitMath.AdjustQuantity(10m, new List<StockSplit>(), D("2020-01-01")));
    }

    [Fact]
    public void AdjustQuantity_ForwardSplit_KeepsFraction()
    {
        // Split normal (rácio ≥ 1) não trunca: mantém frações (ex.: fractional shares).
        var splits = new List<StockSplit> { new(D("2020-08-31"), 4m) };
        Assert.Equal(2m, SplitMath.AdjustQuantity(0.5m, splits, D("2020-01-01"))); // 0,5 × 4 = 2
    }

    [Theory]
    // Reverse 1:20 (AMRN, abr/2025): cada lote é truncado (a corretora liquida a fração).
    [InlineData(250, 12)]  // 12,5 → 12
    [InlineData(28, 1)]    // 1,4  → 1
    [InlineData(50, 2)]    // 2,5  → 2
    [InlineData(172, 8)]   // 8,6  → 8
    public void AdjustQuantity_ReverseSplit_TruncatesFraction(int qty, int expected)
    {
        var splits = new List<StockSplit> { new(D("2025-04-11"), 0.05m) };
        Assert.Equal(expected, SplitMath.AdjustQuantity(qty, splits, D("2024-01-01")));
    }

    [Fact]
    public void AdjustQuantity_AmrnRealCase_PositionClosesToZero()
    {
        // Caso real AMRN: reverse 1:20; compras pré-split truncadas por lote, vendas de dez pós-split.
        var splits = new List<StockSplit> { new(D("2025-04-11"), 0.05m) };
        decimal Buy(int q, string d) => SplitMath.AdjustQuantity(q, splits, D(d));
        var buys = Buy(21, "2021-06-08") + Buy(28, "2021-09-15") + Buy(250, "2021-10-07")
                 + Buy(250, "2023-01-06") + Buy(28, "2025-01-03") + Buy(50, "2025-01-03") + Buy(172, "2025-01-03");
        var sells = Buy(21, "2021-08-12") + Buy(28, "2021-09-24") + Buy(35, "2025-12-18"); // 8+2+12+1+12
        Assert.Equal(37m, buys);
        Assert.Equal(37m, sells);
        Assert.Equal(0m, buys - sells); // vendeu tudo → fecha a zero
    }

    [Fact]
    public void AdjustQuantity_AmcRealCase_PositionClosesToZero()
    {
        // Caso real AMC: reverse 1:10 (ago/2023).
        var splits = new List<StockSplit> { new(D("2023-08-24"), 0.1m) };
        decimal Q(int q, string d) => SplitMath.AdjustQuantity(q, splits, D(d));
        var buys = Q(25, "2021-09-30") + Q(25, "2021-10-01") + Q(13, "2021-11-01")
                 + Q(12, "2021-11-19") + Q(14, "2021-12-08") + Q(6, "2021-12-17")
                 + Q(27, "2024-01-26") + Q(3, "2025-05-27");
        var sells = Q(25, "2021-09-30") + Q(25, "2021-10-01") + Q(13, "2021-11-03")
                  + Q(27, "2025-12-08") + Q(1, "2025-12-08") + Q(3, "2025-12-08") + Q(1, "2025-12-08");
        Assert.Equal(0m, buys - sells); // vendeu tudo → fecha a zero
    }
}
