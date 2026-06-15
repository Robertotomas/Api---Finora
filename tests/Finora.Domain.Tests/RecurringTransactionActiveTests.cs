using Finora.Domain.Entities;

namespace Finora.Domain.Tests;

// Regra do "fim exclusivo": ativa sse início ≤ mês E (sem fim OU fim > mês).
// Espelhada em IsActiveInMonth (instância) e ActiveInMonthExpr (EF) — o último
// teste garante que ambas concordam.
public class RecurringTransactionActiveTests
{
    private static RecurringTransaction Make(
        int startMonth, int startYear, int? endMonth = null, int? endYear = null) => new()
    {
        StartMonth = startMonth,
        StartYear = startYear,
        EndMonth = endMonth,
        EndYear = endYear
    };

    [Fact]
    public void ActiveFromStartMonthInclusive()
    {
        var r = Make(startMonth: 3, startYear: 2025);
        Assert.False(r.IsActiveInMonth(2025, 2)); // mês antes do início
        Assert.True(r.IsActiveInMonth(2025, 3));  // mês de início (inclusivo)
        Assert.True(r.IsActiveInMonth(2025, 4));
    }

    [Fact]
    public void InactiveBeforeStartYear()
    {
        var r = Make(startMonth: 1, startYear: 2025);
        Assert.False(r.IsActiveInMonth(2024, 12));
        Assert.True(r.IsActiveInMonth(2025, 1));
    }

    [Fact]
    public void NoEnd_ActiveIndefinitely()
    {
        var r = Make(startMonth: 1, startYear: 2020);
        Assert.True(r.IsActiveInMonth(2020, 1));
        Assert.True(r.IsActiveInMonth(2030, 12));
    }

    [Fact]
    public void End_IsExclusive_NotActiveInEndMonthItself()
    {
        // Soft-end em junho/2025: ativa até maio, NÃO conta em junho.
        var r = Make(startMonth: 1, startYear: 2025, endMonth: 6, endYear: 2025);
        Assert.True(r.IsActiveInMonth(2025, 5));   // último mês ativo (fim − 1)
        Assert.False(r.IsActiveInMonth(2025, 6));  // mês de fim (exclusivo)
        Assert.False(r.IsActiveInMonth(2025, 7));
    }

    [Fact]
    public void End_NextYear_StillActiveThroughDecember()
    {
        var r = Make(startMonth: 1, startYear: 2025, endMonth: 1, endYear: 2026);
        Assert.True(r.IsActiveInMonth(2025, 12)); // ainda ativa em dezembro
        Assert.False(r.IsActiveInMonth(2026, 1)); // fim exclusivo em jan/2026
    }

    [Fact]
    public void StartAndEndSameMonth_NeverActive()
    {
        // Início e fim no mesmo mês → janela vazia (fim exclusivo).
        var r = Make(startMonth: 4, startYear: 2025, endMonth: 4, endYear: 2025);
        Assert.False(r.IsActiveInMonth(2025, 4));
    }

    // ActiveInMonthExpr (versão EF) tem de devolver exatamente o mesmo que IsActiveInMonth
    // para todas as combinações — senão a query SQL diverge dos cálculos em memória.
    [Fact]
    public void Expr_MatchesInstanceMethod_AcrossGrid()
    {
        int?[] endMonths = { null, 1, 6, 12 };
        int?[] endYears = { null, 2025, 2026 };

        foreach (var startYear in new[] { 2025 })
        foreach (var startMonth in new[] { 1, 6, 12 })
        foreach (var endMonth in endMonths)
        foreach (var endYear in endYears)
        {
            // Estado coerente: fim definido em par (ou ambos null, ou ambos com valor).
            if ((endMonth is null) != (endYear is null)) continue;

            var r = Make(startMonth, startYear, endMonth, endYear);

            foreach (var year in new[] { 2024, 2025, 2026 })
            foreach (var month in new[] { 1, 5, 6, 12 })
            {
                var compiled = RecurringTransaction.ActiveInMonthExpr(year, month).Compile();
                Assert.Equal(r.IsActiveInMonth(year, month), compiled(r));
            }
        }
    }
}
