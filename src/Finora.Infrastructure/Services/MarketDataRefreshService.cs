using Finora.Application.Interfaces;
using Finora.Domain.Entities;

namespace Finora.Infrastructure.Services;

public class MarketDataRefreshService : IMarketDataRefreshService
{
    private readonly IInvestmentRepository _investmentRepository;
    private readonly IInstrumentQuoteRepository _quoteRepository;
    private readonly IMarketDataProvider _provider;

    public MarketDataRefreshService(
        IInvestmentRepository investmentRepository,
        IInstrumentQuoteRepository quoteRepository,
        IMarketDataProvider provider)
    {
        _investmentRepository = investmentRepository;
        _quoteRepository = quoteRepository;
        _provider = provider;
    }

    // Cooldown GLOBAL por símbolo: fora do job diário, só se vai ao Yahoo buscar cotações "velhas"
    // (ou em falta). Como as quotes estão em BD partilhadas por símbolo entre agregados, isto limita o
    // total de chamadas externas independentemente de quantos users carreguem em "Atualizar".
    private static readonly TimeSpan QuoteFreshness = TimeSpan.FromMinutes(10);

    public async Task<int> RefreshAllAsync(CancellationToken cancellationToken = default)
    {
        var symbols = await _investmentRepository.GetDistinctProviderSymbolsAsync(cancellationToken);
        return await RefreshSymbolsAsync(symbols, cancellationToken, force: true);
    }

    public async Task<int> RefreshSymbolsAsync(IEnumerable<string> providerSymbols, CancellationToken cancellationToken = default, bool force = false)
    {
        var symbols = providerSymbols.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
        if (symbols.Count == 0) return 0;

        if (!force)
        {
            // Filtra os símbolos cuja cotação ainda está fresca (atualizada há < QuoteFreshness): esses
            // saem da BD, sem nova chamada externa. Só os velhos/em falta vão ao Yahoo.
            var existing = await _quoteRepository.GetBySymbolsAsync(symbols, cancellationToken);
            var cutoff = DateTime.UtcNow - QuoteFreshness;
            var fresh = existing
                .Where(q => (q.UpdatedAt ?? q.CreatedAt) >= cutoff)
                .Select(q => q.ProviderSymbol)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            symbols = symbols.Where(s => !fresh.Contains(s)).ToList();
            if (symbols.Count == 0) return 0; // tudo fresco → zero chamadas externas
        }

        var quotes = await _provider.GetQuotesAsync(symbols, cancellationToken);
        if (quotes.Count == 0) return 0;

        await _quoteRepository.UpsertAsync(quotes.Select(q => new InstrumentQuote
        {
            ProviderSymbol = q.ProviderSymbol,
            Price = q.Price,
            Currency = q.Currency,
            AsOf = q.AsOf
        }), cancellationToken);

        return quotes.Count;
    }
}
