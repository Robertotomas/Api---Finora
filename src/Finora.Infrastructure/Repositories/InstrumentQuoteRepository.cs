using Finora.Application.Interfaces;
using Finora.Domain.Entities;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Finora.Infrastructure.Repositories;

public class InstrumentQuoteRepository : IInstrumentQuoteRepository
{
    private readonly ApplicationDbContext _context;

    public InstrumentQuoteRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<InstrumentQuote>> GetBySymbolsAsync(IEnumerable<string> providerSymbols, CancellationToken cancellationToken = default)
    {
        var symbols = providerSymbols.Distinct().ToList();
        if (symbols.Count == 0) return Array.Empty<InstrumentQuote>();
        return await _context.InstrumentQuotes
            .AsNoTracking()
            .Where(q => symbols.Contains(q.ProviderSymbol))
            .ToListAsync(cancellationToken);
    }

    public async Task UpsertAsync(IEnumerable<InstrumentQuote> quotes, CancellationToken cancellationToken = default)
    {
        var list = quotes.ToList();
        if (list.Count == 0) return;

        var symbols = list.Select(q => q.ProviderSymbol).Distinct().ToList();
        var existing = await _context.InstrumentQuotes
            .Where(q => symbols.Contains(q.ProviderSymbol))
            .ToDictionaryAsync(q => q.ProviderSymbol, cancellationToken);

        foreach (var q in list)
        {
            if (existing.TryGetValue(q.ProviderSymbol, out var current))
            {
                current.Price = q.Price;
                current.Currency = q.Currency;
                current.AsOf = q.AsOf;
                current.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                _context.InstrumentQuotes.Add(new InstrumentQuote
                {
                    Id = Guid.NewGuid(),
                    ProviderSymbol = q.ProviderSymbol,
                    Price = q.Price,
                    Currency = q.Currency,
                    AsOf = q.AsOf,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
