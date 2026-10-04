using Finora.Domain.Entities;
using Finora.Domain.Enums;

namespace Finora.Infrastructure.Services;

/// <summary>
/// Dinheiro parado na corretora ("por investir"): o que foi depositado e ainda não foi gasto em
/// compras. Conta para o Património Total — sem isto, entre o depósito e a compra (ou com sobras)
/// o dinheiro "desaparecia": já saiu da conta, mas ainda não é uma posição. Lógica pura, testável.
/// </summary>
public static class BrokerCashMath
{
    /// <summary>Custo de uma compra em EUR (preço + comissão, ao câmbio da transação, com a fee de câmbio).</summary>
    public static decimal BuyCostEur(InvestmentTransaction t)
        => (t.Quantity * t.UnitPrice + t.Commission) * t.FxRateToEur * (1m + t.FxFeePercent / 100m);

    /// <summary>Receita líquida de uma venda em EUR (preço − comissão, ao câmbio da transação, menos a fee de câmbio).</summary>
    public static decimal SellProceedsEur(InvestmentTransaction t)
        => (t.Quantity * t.UnitPrice - t.Commission) * t.FxRateToEur * (1m - t.FxFeePercent / 100m);

    /// <summary>
    /// Depósitos líquidos (EUR) − compras + vendas, com clamp a ≥ 0. <b>Sem depósitos registados devolve 0</b>:
    /// o utilizador não está a seguir o dinheiro da corretora, por isso não inventamos um saldo a partir
    /// só das transações. Negativo (ex.: compras anteriores aos depósitos registados) também dá 0.
    /// Dividendos/juros/taxas da corretora não são registados, por isso é uma aproximação.
    /// </summary>
    public static decimal UninvestedCashEur(bool hasDeposits, decimal netDepositsEur, IEnumerable<InvestmentTransaction> transactions)
    {
        if (!hasDeposits) return 0m;
        var cash = netDepositsEur;
        foreach (var t in transactions)
            cash += t.Operation == InvestmentOperation.Buy ? -BuyCostEur(t) : SellProceedsEur(t);
        return Math.Max(0m, Math.Round(cash, 2));
    }
}
