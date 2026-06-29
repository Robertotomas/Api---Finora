using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Finora.Application.Interfaces;
using Finora.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Finora.Infrastructure.Services;

/// <summary>
/// Taxas de câmbio para EUR via frankfurter.app (BCE, grátis, sem chave).
/// Devolve multiplicadores moeda → EUR (EUR = 1). Cache em memória por data.
/// </summary>
public class FxRateService : IFxRateService
{
    private readonly HttpClient _http;
    private readonly MarketDataOptions _options;
    private readonly ILogger<FxRateService> _logger;

    // Cache por chave de data ("latest" ou "yyyy-MM-dd"). As históricas são imutáveis.
    private static readonly ConcurrentDictionary<string, Dictionary<string, decimal>> Cache = new();
    private static readonly SemaphoreSlim Lock = new(1, 1);
    private static DateTime _latestDateUtc = DateTime.MinValue;

    public FxRateService(HttpClient http, IOptions<MarketDataOptions> options, ILogger<FxRateService> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyDictionary<string, decimal>> GetRatesToEurAsync(CancellationToken cancellationToken = default)
    {
        var today = DateTime.UtcNow.Date;
        if (_latestDateUtc == today && Cache.TryGetValue("latest", out var cached)) return cached;

        await Lock.WaitAsync(cancellationToken);
        try
        {
            if (_latestDateUtc == today && Cache.TryGetValue("latest", out var c)) return c;
            var map = await FetchAsync($"{_options.FxBaseUrl}/latest?from=EUR", cancellationToken);
            Cache["latest"] = map;
            _latestDateUtc = today;
            return map;
        }
        finally
        {
            Lock.Release();
        }
    }

    public async Task<decimal> GetRateToEurAsync(string currency, DateTime date, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(currency) || string.Equals(currency, "EUR", StringComparison.OrdinalIgnoreCase))
            return 1m;

        var key = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (!Cache.TryGetValue(key, out var map))
        {
            await Lock.WaitAsync(cancellationToken);
            try
            {
                if (!Cache.TryGetValue(key, out map))
                {
                    map = await FetchAsync($"{_options.FxBaseUrl}/{key}?from=EUR", cancellationToken);
                    Cache[key] = map;
                }
            }
            finally
            {
                Lock.Release();
            }
        }

        if (map.TryGetValue(currency, out var rate)) return rate;

        // Fallback: taxa de hoje, depois 1.
        var todayRates = await GetRatesToEurAsync(cancellationToken);
        return todayRates.TryGetValue(currency, out var r) ? r : 1m;
    }

    // Cache da série histórica por chave "ccy|from|to" (imutável depois de fechada).
    private static readonly ConcurrentDictionary<string, Dictionary<DateOnly, decimal>> SeriesCache = new();

    public async Task<IReadOnlyDictionary<DateOnly, decimal>> GetRateSeriesToEurAsync(string currency, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var empty = new Dictionary<DateOnly, decimal>();
        if (string.IsNullOrWhiteSpace(currency) || string.Equals(currency, "EUR", StringComparison.OrdinalIgnoreCase))
            return empty;
        if (to < from) return empty;

        var ccy = currency.ToUpperInvariant();
        var key = $"{ccy}|{from:yyyy-MM-dd}|{to:yyyy-MM-dd}";
        if (SeriesCache.TryGetValue(key, out var cached)) return cached;

        var url = $"{_options.FxBaseUrl}/{from:yyyy-MM-dd}..{to:yyyy-MM-dd}?from=EUR&to={ccy}";
        var map = new Dictionary<DateOnly, decimal>();
        try
        {
            using var resp = await _http.GetAsync(url, cancellationToken);
            if (resp.IsSuccessStatusCode)
            {
                await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                if (doc.RootElement.TryGetProperty("rates", out var rates) && rates.ValueKind == JsonValueKind.Object)
                {
                    foreach (var day in rates.EnumerateObject())
                    {
                        if (!DateOnly.TryParseExact(day.Name, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                            continue;
                        if (day.Value.ValueKind == JsonValueKind.Object &&
                            day.Value.TryGetProperty(ccy, out var rateEl) &&
                            rateEl.ValueKind == JsonValueKind.Number &&
                            rateEl.TryGetDecimal(out var eurToX) && eurToX != 0)
                        {
                            map[d] = 1m / eurToX; // X → EUR
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FX series fetch failed for {Url}; using EUR-only.", url);
        }

        SeriesCache[key] = map;
        return map;
    }

    /// <summary>Lê EUR→X do frankfurter e devolve X→EUR (inverso). EUR = 1.</summary>
    private async Task<Dictionary<string, decimal>> FetchAsync(string url, CancellationToken cancellationToken)
    {
        var map = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["EUR"] = 1m };
        try
        {
            using var resp = await _http.GetAsync(url, cancellationToken);
            if (resp.IsSuccessStatusCode)
            {
                await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                if (doc.RootElement.TryGetProperty("rates", out var rates) && rates.ValueKind == JsonValueKind.Object)
                {
                    foreach (var r in rates.EnumerateObject())
                    {
                        if (r.Value.ValueKind == JsonValueKind.Number && r.Value.TryGetDecimal(out var eurToX) && eurToX != 0)
                            map[r.Name] = 1m / eurToX;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FX rate fetch failed for {Url}; using EUR-only.", url);
        }
        return map;
    }
}
