using Finora.Application.Interfaces;

namespace Finora.Infrastructure.Services;

/// <summary>
/// Ajuste de quantidades a desdobramentos (stock splits). Os preços do Yahoo já vêm ajustados a
/// splits (escala atual), por isso a quantidade detida tem de ser multiplicada pelo mesmo fator
/// para o valor (quantidade × preço) bater certo. Lógica pura, testável.
/// </summary>
public static class SplitMath
{
    /// <summary>
    /// Fator acumulado dos splits ocorridos ESTRITAMENTE depois de <paramref name="txDate"/>. Uma
    /// quantidade comprada nessa data, multiplicada por este fator, fica na escala atual (pós-split).
    /// Sem splits posteriores → 1. Ex.: comprei antes de um split 4:1 → fator 4 (1 ação = 4 hoje).
    /// </summary>
    public static decimal FactorAfter(IReadOnlyList<StockSplit> splits, DateOnly txDate)
    {
        if (splits is null || splits.Count == 0) return 1m;
        var factor = 1m;
        foreach (var s in splits)
            if (s.Date > txDate && s.Ratio > 0m) factor *= s.Ratio;
        return factor;
    }

    /// <summary>
    /// Quantidade de uma transação convertida para a escala atual, aplicando cada split posterior
    /// pela ORDEM cronológica. Num <b>reverse split</b> (rácio &lt; 1) a fração resultante é
    /// <b>truncada</b> (as corretoras liquidam-na em dinheiro — "cash-in-lieu"), senão sobrariam
    /// frações fantasma que impedem uma posição totalmente vendida de fechar a zero. Splits normais
    /// (rácio ≥ 1) não truncam. Aplica-se por transação (a corretora arredonda cada lote de compra).
    /// </summary>
    public static decimal AdjustQuantity(decimal quantity, IReadOnlyList<StockSplit> splits, DateOnly txDate)
    {
        if (splits is null || splits.Count == 0) return quantity;
        var q = quantity;
        foreach (var s in splits.OrderBy(x => x.Date))
        {
            if (s.Ratio <= 0m || s.Date <= txDate) continue;
            q *= s.Ratio;
            if (s.Ratio < 1m) q = Math.Floor(q); // reverse split → fração liquidada em dinheiro
        }
        return q;
    }
}
