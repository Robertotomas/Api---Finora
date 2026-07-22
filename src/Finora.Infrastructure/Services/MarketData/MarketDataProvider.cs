using System.Globalization;
using System.Text.Json;
using Finora.Application.Interfaces;
using Finora.Application.Options;
using Finora.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Finora.Infrastructure.Services.MarketData;

/// <summary>
/// Pesquisa de símbolos via Twelve Data (com chave) + preços via Yahoo (não-oficial, sem chave),
/// porque o plano gratuito da Twelve Data não cobre cotações de bolsas europeias.
/// </summary>
public class MarketDataProvider : IMarketDataProvider
{
    private readonly HttpClient _http;
    private readonly MarketDataOptions _options;
    private readonly LogoDevOptions _logoOptions;
    private readonly ILogger<MarketDataProvider> _logger;

    // Cache de domínios resolvidos por nome (evita repetir chamadas à Logo.dev).
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string?> DomainCache = new(StringComparer.OrdinalIgnoreCase);

    public MarketDataProvider(HttpClient http, IOptions<MarketDataOptions> options, IOptions<LogoDevOptions> logoOptions, ILogger<MarketDataProvider> logger)
    {
        _http = http;
        _options = options.Value;
        _logoOptions = logoOptions.Value;
        _logger = logger;
    }

    public async Task<string?> ResolveBrandDomainAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(_logoOptions.SecretKey))
            return null;

        var key = query.Trim();
        if (DomainCache.TryGetValue(key, out var cached)) return cached;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{_logoOptions.BaseUrl}/search?q={Uri.EscapeDataString(key)}");
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_logoOptions.SecretKey}");
            using var resp = await _http.SendAsync(req, cancellationToken);
            if (!resp.IsSuccessStatusCode) { DomainCache[key] = null; return null; }

            await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            string? domain = null;
            if (doc.RootElement.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                var first = arr.EnumerateArray().FirstOrDefault();
                if (first.ValueKind == JsonValueKind.Object) domain = Str(first, "domain");
            }
            domain = string.IsNullOrWhiteSpace(domain) ? null : domain;
            DomainCache[key] = domain;
            return domain;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Logo.dev brand search failed for '{Query}'", query);
            return null;
        }
    }

    public async Task<IReadOnlyList<InstrumentSearchResult>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(_options.ApiKey))
            return Array.Empty<InstrumentSearchResult>();

        // "VUAA.DE" → "VUAA": a Twelve Data pesquisa pelo símbolo base, não pelo sufixo de bolsa.
        var q = YahooSymbolMap.StripExchangeSuffix(query.Trim());
        var url = $"{_options.BaseUrl}/symbol_search?symbol={Uri.EscapeDataString(q)}&outputsize=30&apikey={_options.ApiKey}";
        try
        {
            using var resp = await _http.GetAsync(url, cancellationToken);
            if (!resp.IsSuccessStatusCode) return Array.Empty<InstrumentSearchResult>();
            await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return Array.Empty<InstrumentSearchResult>();

            var results = new List<InstrumentSearchResult>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in data.EnumerateArray())
            {
                var symbol = Str(item, "symbol");
                var mic = Str(item, "mic_code");
                if (string.IsNullOrWhiteSpace(symbol)) continue;
                var providerSymbol = YahooSymbolMap.ToYahoo(symbol, mic);
                if (!seen.Add(providerSymbol)) continue; // mesmo símbolo Yahoo → cotação igual

                results.Add(new InstrumentSearchResult(
                    Symbol: symbol,
                    Name: Str(item, "instrument_name"),
                    Exchange: Str(item, "exchange"),
                    MicCode: mic,
                    Currency: string.IsNullOrWhiteSpace(Str(item, "currency")) ? "EUR" : Str(item, "currency"),
                    Type: MapType(Str(item, "instrument_type")),
                    ProviderSymbol: providerSymbol));

                if (results.Count >= 15) break;
            }
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Symbol search failed for '{Query}'", query);
            return Array.Empty<InstrumentSearchResult>();
        }
    }

    public async Task<IReadOnlyList<MarketQuote>> GetQuotesAsync(IEnumerable<string> providerSymbols, CancellationToken cancellationToken = default)
    {
        var symbols = providerSymbols.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
        if (symbols.Count == 0) return Array.Empty<MarketQuote>();

        // Em paralelo, com concorrência limitada (evita N round-trips sequenciais ao Yahoo).
        using var gate = new SemaphoreSlim(6);
        var tasks = symbols.Select(async sym =>
        {
            await gate.WaitAsync(cancellationToken);
            try { return await FetchYahooQuoteAsync(sym, cancellationToken); }
            finally { gate.Release(); }
        });
        var results = await Task.WhenAll(tasks);
        return results.Where(q => q != null).Select(q => q!).ToList();
    }

    // Cache do histórico POR SÍMBOLO, cobrindo o intervalo máximo já pedido. Assim uma só chamada
    // Yahoo serve todos os períodos (YTD/3M/.../5A): fatia-se em memória em vez de pedir por cada from/to.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime CachedAt, DateOnly From, DateOnly To, IReadOnlyList<PricePoint> Points)> HistoryCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan HistoryTtl = TimeSpan.FromHours(6);

    public async Task<IReadOnlyList<PricePoint>> GetHistoryAsync(string providerSymbol, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerSymbol) || to < from) return Array.Empty<PricePoint>();

        var hasEntry = HistoryCache.TryGetValue(providerSymbol, out var entry);
        // Cache fresca que cobre o pedido → fatia em memória, sem ir ao Yahoo.
        if (hasEntry && DateTime.UtcNow - entry.CachedAt < HistoryTtl && entry.From <= from && entry.To >= to)
            return Slice(entry.Points, from, to);

        // Alarga o fetch para cobrir o que já estava em cache (o intervalo cresce monotonicamente).
        var fetchFrom = from;
        var fetchTo = to;
        if (hasEntry)
        {
            if (entry.From < fetchFrom) fetchFrom = entry.From;
            if (entry.To > fetchTo) fetchTo = entry.To;
        }

        // Yahoo aceita period1/period2 em segundos unix (period2 exclusivo → +1 dia).
        var p1 = new DateTimeOffset(fetchFrom.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
        var p2 = new DateTimeOffset(fetchTo.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
        var url = $"{_options.YahooBaseUrl}/v8/finance/chart/{Uri.EscapeDataString(providerSymbol)}?period1={p1}&period2={p2}&interval=1d";

        try
        {
            using var resp = await _http.GetAsync(url, cancellationToken);
            if (!resp.IsSuccessStatusCode) return Array.Empty<PricePoint>();
            await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!doc.RootElement.TryGetProperty("chart", out var chart)) return Array.Empty<PricePoint>();
            if (!chart.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array) return Array.Empty<PricePoint>();
            var first = result.EnumerateArray().FirstOrDefault();
            if (first.ValueKind != JsonValueKind.Object) return Array.Empty<PricePoint>();

            if (!first.TryGetProperty("timestamp", out var ts) || ts.ValueKind != JsonValueKind.Array) return Array.Empty<PricePoint>();
            if (!first.TryGetProperty("indicators", out var indicators)) return Array.Empty<PricePoint>();
            if (!indicators.TryGetProperty("quote", out var quoteArr) || quoteArr.ValueKind != JsonValueKind.Array) return Array.Empty<PricePoint>();
            var quote0 = quoteArr.EnumerateArray().FirstOrDefault();
            if (quote0.ValueKind != JsonValueKind.Object || !quote0.TryGetProperty("close", out var closes) || closes.ValueKind != JsonValueKind.Array)
                return Array.Empty<PricePoint>();

            var times = ts.EnumerateArray().ToList();
            var closeList = closes.EnumerateArray().ToList();
            var n = Math.Min(times.Count, closeList.Count);
            var points = new List<PricePoint>(n);
            for (var i = 0; i < n; i++)
            {
                if (times[i].ValueKind != JsonValueKind.Number) continue;
                if (!TryDecimal(closeList[i], out var close)) continue; // nulls (feriados) saltam
                var d = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(times[i].GetInt64()).UtcDateTime);
                points.Add(new PricePoint(d, close));
            }

            IReadOnlyList<PricePoint> ordered = points.OrderBy(p => p.Date).ToList();
            HistoryCache[providerSymbol] = (DateTime.UtcNow, fetchFrom, fetchTo, ordered);
            return Slice(ordered, from, to);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "History fetch failed for '{Symbol}'", providerSymbol);
            // Falha de rede → reaproveita a cache anterior (mesmo expirada) se cobrir o pedido.
            if (hasEntry && entry.From <= from && entry.To >= to) return Slice(entry.Points, from, to);
            return Array.Empty<PricePoint>();
        }
    }

    // Devolve só os pontos dentro de [from, to] (a lista já vem ordenada por data).
    private static IReadOnlyList<PricePoint> Slice(IReadOnlyList<PricePoint> points, DateOnly from, DateOnly to)
    {
        if (points.Count == 0) return points;
        var result = new List<PricePoint>(points.Count);
        foreach (var p in points)
            if (p.Date >= from && p.Date <= to) result.Add(p);
        return result;
    }

    // Cache de splits por símbolo (raramente mudam; TTL 6h, partilhada entre agregados).
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime CachedAt, IReadOnlyList<StockSplit> Splits)> SplitsCache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<StockSplit>> GetSplitsAsync(string providerSymbol, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerSymbol)) return Array.Empty<StockSplit>();
        if (SplitsCache.TryGetValue(providerSymbol, out var hit) && DateTime.UtcNow - hit.CachedAt < HistoryTtl)
            return hit.Splits;

        // Pede o histórico completo (desde 1970) só pelos eventos de split; interval=1mo minimiza o payload de preços.
        var p2 = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(1), TimeSpan.Zero).ToUnixTimeSeconds();
        var url = $"{_options.YahooBaseUrl}/v8/finance/chart/{Uri.EscapeDataString(providerSymbol)}?period1=0&period2={p2}&interval=1mo&events=splits";

        try
        {
            using var resp = await _http.GetAsync(url, cancellationToken);
            if (!resp.IsSuccessStatusCode) return CacheSplits(providerSymbol, Array.Empty<StockSplit>());
            await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!doc.RootElement.TryGetProperty("chart", out var chart)) return CacheSplits(providerSymbol, Array.Empty<StockSplit>());
            if (!chart.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array) return CacheSplits(providerSymbol, Array.Empty<StockSplit>());
            var first = result.EnumerateArray().FirstOrDefault();
            if (first.ValueKind != JsonValueKind.Object) return CacheSplits(providerSymbol, Array.Empty<StockSplit>());
            if (!first.TryGetProperty("events", out var events) || !events.TryGetProperty("splits", out var splits) || splits.ValueKind != JsonValueKind.Object)
                return CacheSplits(providerSymbol, Array.Empty<StockSplit>());

            var list = new List<StockSplit>();
            foreach (var ev in splits.EnumerateObject())
            {
                var e = ev.Value;
                if (e.ValueKind != JsonValueKind.Object) continue;
                if (!e.TryGetProperty("date", out var dEl) || dEl.ValueKind != JsonValueKind.Number) continue;
                if (!e.TryGetProperty("numerator", out var numEl) || !TryDecimal(numEl, out var num) || num <= 0) continue;
                if (!e.TryGetProperty("denominator", out var denEl) || !TryDecimal(denEl, out var den) || den <= 0) continue;
                var date = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(dEl.GetInt64()).UtcDateTime);
                list.Add(new StockSplit(date, num / den));
            }
            IReadOnlyList<StockSplit> ordered = list.OrderBy(s => s.Date).ToList();
            return CacheSplits(providerSymbol, ordered);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Splits fetch failed for '{Symbol}'", providerSymbol);
            if (SplitsCache.TryGetValue(providerSymbol, out var stale)) return stale.Splits; // reaproveita mesmo expirado
            return Array.Empty<StockSplit>();
        }
    }

    private static IReadOnlyList<StockSplit> CacheSplits(string symbol, IReadOnlyList<StockSplit> splits)
    {
        SplitsCache[symbol] = (DateTime.UtcNow, splits);
        return splits;
    }

    private async Task<MarketQuote?> FetchYahooQuoteAsync(string providerSymbol, CancellationToken cancellationToken)
    {
        var url = $"{_options.YahooBaseUrl}/v8/finance/chart/{Uri.EscapeDataString(providerSymbol)}?range=1d&interval=1d";
        try
        {
            using var resp = await _http.GetAsync(url, cancellationToken);
            if (!resp.IsSuccessStatusCode) return null;
            await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!doc.RootElement.TryGetProperty("chart", out var chart)) return null;
            if (!chart.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array) return null;
            var first = result.EnumerateArray().FirstOrDefault();
            if (first.ValueKind != JsonValueKind.Object) return null;
            if (!first.TryGetProperty("meta", out var meta)) return null;

            if (!meta.TryGetProperty("regularMarketPrice", out var priceEl) ||
                !TryDecimal(priceEl, out var price))
                return null;

            var currency = meta.TryGetProperty("currency", out var curEl) && curEl.ValueKind == JsonValueKind.String
                ? curEl.GetString() ?? "EUR"
                : "EUR";

            var asOf = DateTime.UtcNow;
            if (meta.TryGetProperty("regularMarketTime", out var t) && t.ValueKind == JsonValueKind.Number)
                asOf = DateTimeOffset.FromUnixTimeSeconds(t.GetInt64()).UtcDateTime;

            return new MarketQuote(providerSymbol, price, currency, asOf);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Quote fetch failed for '{Symbol}'", providerSymbol);
            return null;
        }
    }

    private static string Str(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static bool TryDecimal(JsonElement el, out decimal value)
    {
        value = 0m;
        if (el.ValueKind == JsonValueKind.Number)
        {
            if (el.TryGetDecimal(out value)) return true;
            value = (decimal)el.GetDouble();
            return true;
        }
        if (el.ValueKind == JsonValueKind.String)
            return decimal.TryParse(el.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value);
        return false;
    }

    private static InstrumentType MapType(string instrumentType)
    {
        if (string.IsNullOrWhiteSpace(instrumentType)) return InstrumentType.Other;
        var t = instrumentType.Trim();
        if (t.Contains("ETF", StringComparison.OrdinalIgnoreCase)) return InstrumentType.Etf;
        if (t.Contains("Stock", StringComparison.OrdinalIgnoreCase) || t.Contains("Equity", StringComparison.OrdinalIgnoreCase))
            return InstrumentType.Stock;
        return InstrumentType.Other;
    }
}
