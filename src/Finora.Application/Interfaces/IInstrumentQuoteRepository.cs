using Finora.Domain.Entities;

namespace Finora.Application.Interfaces;

public interface IInstrumentQuoteRepository
{
    Task<IReadOnlyList<InstrumentQuote>> GetBySymbolsAsync(IEnumerable<string> providerSymbols, CancellationToken cancellationToken = default);

    /// <summary>Insere ou atualiza a cotação de cada símbolo (chave = ProviderSymbol).</summary>
    Task UpsertAsync(IEnumerable<InstrumentQuote> quotes, CancellationToken cancellationToken = default);
}
