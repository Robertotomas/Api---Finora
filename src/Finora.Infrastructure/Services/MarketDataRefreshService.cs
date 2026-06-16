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

    public async Task<int> RefreshAllAsync(CancellationToken cancellationToken = default)
    {
        var symbols = await _investmentRepository.GetDistinctProviderSymbolsAsync(cancellationToken);
        return await RefreshSymbolsAsync(symbols, cancellationToken);
    }

    public async Task<int> RefreshSymbolsAsync(IEnumerable<string> providerSymbols, CancellationToken cancellationToken = default)
    {
        var symbols = providerSymbols.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
        if (symbols.Count == 0) return 0;

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
