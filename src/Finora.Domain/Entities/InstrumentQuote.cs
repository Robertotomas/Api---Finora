using Finora.Domain.Common;

namespace Finora.Domain.Entities;

/// <summary>
/// Cache partilhado de cotações por símbolo (uma linha por ticker, não por agregado).
/// Atualizado pelo job diário de market data. Evita chamadas repetidas à API externa.
/// </summary>
public class InstrumentQuote : BaseEntity
{
    /// <summary>Símbolo do fornecedor de preços (Yahoo), ex.: "VWCE.DE". Único.</summary>
    public string ProviderSymbol { get; set; } = string.Empty;

    public decimal Price { get; set; }

    /// <summary>Moeda do preço (ex.: "EUR", "USD").</summary>
    public string Currency { get; set; } = "EUR";

    /// <summary>Data a que o preço se refere (fecho).</summary>
    public DateTime AsOf { get; set; }
}
