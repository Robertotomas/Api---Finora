using Finora.Domain.Entities;
using Finora.Domain.Enums;

namespace Finora.Domain.Tests;

// Cobre a regra de cálculo partilhada pelos 5 pontos de agregação do backend.
// Tem de ficar em sincronia com recurringAmountForMonth no frontend
// (App---Finora/src/types/recurringTransaction.ts).
public class RecurringTransactionTests
{
    private static RecurringTransaction Make(
        RecurringFrequency freq, decimal amount, int? annualMonth = null) => new()
    {
        Frequency = freq,
        Amount = amount,
        AnnualMonth = annualMonth
    };

    // --- OccurrencesPerYear ---

    [Theory]
    [InlineData(RecurringFrequency.Monthly, 12)]
    [InlineData(RecurringFrequency.Quarterly, 4)]
    [InlineData(RecurringFrequency.SemiAnnual, 2)]
    [InlineData(RecurringFrequency.Annual, 1)]
    public void OccurrencesPerYear_MatchesFrequency(RecurringFrequency freq, int expected)
    {
        Assert.Equal(expected, Make(freq, 100m).OccurrencesPerYear);
    }

    // --- Mensal: montante inteiro todos os meses ---

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(12)]
    public void Monthly_ReturnsFullAmountEveryMonth(int month)
    {
        var r = Make(RecurringFrequency.Monthly, 50m);
        Assert.Equal(50m, r.AmountForMonth(month));
    }

    // --- Não-mensal diluído (AnnualMonth == null): montante × ocorrências / 12 ---

    [Theory]
    [InlineData(RecurringFrequency.Annual, 1200, 100)]    // 1200 × 1 / 12
    [InlineData(RecurringFrequency.Quarterly, 300, 100)]  // 300 × 4 / 12
    [InlineData(RecurringFrequency.SemiAnnual, 600, 100)] // 600 × 2 / 12
    public void NonMonthly_Diluted_SpreadsEvenlyAcrossAllMonths(
        RecurringFrequency freq, decimal amount, decimal expectedPerMonth)
    {
        var r = Make(freq, amount, annualMonth: null);
        for (var month = 1; month <= 12; month++)
            Assert.Equal(expectedPerMonth, r.AmountForMonth(month));
    }

    [Fact]
    public void NonMonthly_Diluted_RoundsToTwoDecimals()
    {
        // 100 × 1 / 12 = 8.3333... → 8.33
        var r = Make(RecurringFrequency.Annual, 100m, annualMonth: null);
        Assert.Equal(8.33m, r.AmountForMonth(3));
    }

    // --- Anual real (AnnualMonth definido): inteiro só no mês de referência ---

    [Fact]
    public void Annual_Real_FullAmountOnlyOnReferenceMonth()
    {
        var r = Make(RecurringFrequency.Annual, 1200m, annualMonth: 4);
        Assert.Equal(1200m, r.AmountForMonth(4));
        Assert.Equal(0m, r.AmountForMonth(3));
        Assert.Equal(0m, r.AmountForMonth(5));
        Assert.Equal(0m, r.AmountForMonth(1));
    }

    // --- Trimestral real: inteiro no mês de referência e a cada 3 meses ---

    [Theory]
    [InlineData(2, 200)]   // referência
    [InlineData(5, 200)]   // +3
    [InlineData(8, 200)]   // +6
    [InlineData(11, 200)]  // +9
    [InlineData(3, 0)]
    [InlineData(1, 0)]
    [InlineData(12, 0)]
    public void Quarterly_Real_FullAmountOnPaymentMonths(int month, decimal expected)
    {
        var r = Make(RecurringFrequency.Quarterly, 200m, annualMonth: 2);
        Assert.Equal(expected, r.AmountForMonth(month));
    }

    // --- Semestral real: mês de referência e +6 (wrap-around do ano) ---

    [Theory]
    [InlineData(10, 600)]  // referência
    [InlineData(4, 600)]   // +6 (wrap)
    [InlineData(1, 0)]
    [InlineData(7, 0)]
    public void SemiAnnual_Real_WrapsAroundYear(int month, decimal expected)
    {
        var r = Make(RecurringFrequency.SemiAnnual, 600m, annualMonth: 10);
        Assert.Equal(expected, r.AmountForMonth(month));
    }
}
