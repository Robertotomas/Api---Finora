using Finora.Domain.Common;
using Finora.Domain.Enums;

namespace Finora.Domain.Entities;

/// <summary>
/// Uma posição de investimento (ações/ETFs) do agregado. O preço atual vem do cache partilhado
/// <see cref="InstrumentQuote"/> (atualizado 1×/dia). O valor (qty × preço, convertido para EUR)
/// conta para o Património Total.
/// </summary>
public class InvestmentHolding : BaseEntity
{
    /// <summary>Símbolo de origem (Twelve Data), ex.: "VWCE".</summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>MIC/bolsa, ex.: "XETR".</summary>
    public string Exchange { get; set; } = string.Empty;

    /// <summary>Símbolo usado para ir buscar o preço (Yahoo), ex.: "VWCE.DE". Chave do cache de cotações.</summary>
    public string ProviderSymbol { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Domínio da marca para o logo (ex.: "adidas.com"). Opcional; só camada visual.</summary>
    public string? LogoDomain { get; set; }

    /// <summary>Moeda de negociação do instrumento (ex.: "EUR", "USD").</summary>
    public string Currency { get; set; } = "EUR";

    public InstrumentType Type { get; set; }

    public Guid HouseholdId { get; set; }
    public Household Household { get; set; } = null!;

    public ICollection<InvestmentTransaction> Transactions { get; set; } = new List<InvestmentTransaction>();

    /// <summary>Quantidade líquida = compras − vendas.</summary>
    public decimal NetQuantity => Transactions.Sum(t => t.Operation == InvestmentOperation.Buy ? t.Quantity : -t.Quantity);

    /// <summary>Custo médio por unidade (das compras, incluindo comissões).</summary>
    public decimal AverageCost
    {
        get
        {
            var buyQty = Transactions.Where(t => t.Operation == InvestmentOperation.Buy).Sum(t => t.Quantity);
            if (buyQty <= 0) return 0m;
            var buyCost = Transactions.Where(t => t.Operation == InvestmentOperation.Buy)
                .Sum(t => t.Quantity * t.UnitPrice + t.Commission);
            return buyCost / buyQty;
        }
    }

    /// <summary>Custo da posição atual (na moeda do instrumento) = quantidade líquida × custo médio.</summary>
    public decimal InvestedCost => NetQuantity * AverageCost;
}
