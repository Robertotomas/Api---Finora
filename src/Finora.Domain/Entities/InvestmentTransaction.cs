using Finora.Domain.Common;
using Finora.Domain.Enums;

namespace Finora.Domain.Entities;

/// <summary>
/// Uma transação (compra/venda) de uma posição. A quantidade e o custo médio da posição
/// são calculados a partir destas transações.
/// </summary>
public class InvestmentTransaction : BaseEntity
{
    public Guid InvestmentHoldingId { get; set; }
    public InvestmentHolding InvestmentHolding { get; set; } = null!;

    public InvestmentOperation Operation { get; set; }
    public DateTime Date { get; set; }
    public decimal Quantity { get; set; }

    /// <summary>Preço por unidade, na moeda do instrumento.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>Comissão da transação, na moeda do instrumento.</summary>
    public decimal Commission { get; set; }

    /// <summary>Taxa de câmbio moeda-do-instrumento → EUR à data da transação (mid, BCE). 1 se for EUR.</summary>
    public decimal FxRateToEur { get; set; } = 1m;

    /// <summary>Margem de câmbio do broker em % (ex.: 0,5% na XTB). 0 se for EUR.</summary>
    public decimal FxFeePercent { get; set; } = 0m;
}
