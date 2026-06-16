using Finora.Domain.Enums;

namespace Finora.Application.Interfaces;

/// <summary>Resultado da pesquisa de símbolos (Twelve Data).</summary>
public record InstrumentSearchResult(
    string Symbol,
    string Name,
    string Exchange,
    string MicCode,
    string Currency,
    InstrumentType Type,
    string ProviderSymbol);

/// <summary>Cotação devolvida pelo fornecedor de preços (Yahoo).</summary>
public record MarketQuote(
    string ProviderSymbol,
    decimal Price,
    string Currency,
    DateTime AsOf);

/// <summary>Preço de fecho diário (histórico), na moeda do instrumento.</summary>
public record PricePoint(DateOnly Date, decimal Close);

public interface IMarketDataProvider
{
    /// <summary>Pesquisa instrumentos por nome/símbolo (catálogo da Twelve Data).</summary>
    Task<IReadOnlyList<InstrumentSearchResult>> SearchAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>Cotações atuais (fecho) para os símbolos do fornecedor de preços (Yahoo). Símbolos que falham são omitidos.</summary>
    Task<IReadOnlyList<MarketQuote>> GetQuotesAsync(IEnumerable<string> providerSymbols, CancellationToken cancellationToken = default);

    /// <summary>Histórico de fechos diários (Yahoo) para um símbolo, ordenado por data asc. Vazio se falhar.</summary>
    Task<IReadOnlyList<PricePoint>> GetHistoryAsync(string providerSymbol, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>Resolve o domínio da marca de uma empresa (Logo.dev search), para o logo. Null se não houver / sem chave.</summary>
    Task<string?> ResolveBrandDomainAsync(string query, CancellationToken cancellationToken = default);
}
