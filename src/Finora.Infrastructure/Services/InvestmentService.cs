using System.Globalization;
using Finora.Application.DTOs.Investment;
using Finora.Application.Interfaces;
using Finora.Domain.Entities;
using Finora.Domain.Enums;
using Finora.Infrastructure.Services.MarketData;

namespace Finora.Infrastructure.Services;

public class InvestmentService : IInvestmentService
{
    private readonly IInvestmentRepository _investmentRepository;
    private readonly IInstrumentQuoteRepository _quoteRepository;
    private readonly IUserRepository _userRepository;
    private readonly IFxRateService _fxRateService;
    private readonly IMarketDataProvider _marketDataProvider;
    private readonly IMarketDataRefreshService _refreshService;

    public InvestmentService(
        IInvestmentRepository investmentRepository,
        IInstrumentQuoteRepository quoteRepository,
        IUserRepository userRepository,
        IFxRateService fxRateService,
        IMarketDataProvider marketDataProvider,
        IMarketDataRefreshService refreshService)
    {
        _investmentRepository = investmentRepository;
        _quoteRepository = quoteRepository;
        _userRepository = userRepository;
        _fxRateService = fxRateService;
        _marketDataProvider = marketDataProvider;
        _refreshService = refreshService;
    }

    public async Task<IReadOnlyList<InvestmentHoldingDto>> GetByHouseholdAsync(Guid householdId, Guid userId, CancellationToken cancellationToken = default)
    {
        if (!await UserBelongsToHouseholdAsync(userId, householdId, cancellationToken))
            return Array.Empty<InvestmentHoldingDto>();

        var holdings = await _investmentRepository.GetByHouseholdIdAsync(householdId, cancellationToken);
        if (holdings.Count == 0) return Array.Empty<InvestmentHoldingDto>();

        var quotes = await GetQuoteMapAsync(holdings.Select(h => h.ProviderSymbol), cancellationToken);
        var rates = await _fxRateService.GetRatesToEurAsync(cancellationToken);
        return holdings.Select(h => ToDto(h, quotes.GetValueOrDefault(h.ProviderSymbol), rates)).ToList();
    }

    public async Task<InvestmentHoldingDto?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var holding = await _investmentRepository.GetByIdAsync(id, cancellationToken);
        if (holding == null) return null;
        if (!await UserBelongsToHouseholdAsync(userId, holding.HouseholdId, cancellationToken)) return null;
        return await BuildDtoAsync(holding, cancellationToken);
    }

    public async Task<InvestmentHoldingDto?> AddTransactionAsync(AddTransactionRequest request, Guid householdId, Guid userId, CancellationToken cancellationToken = default)
    {
        if (!await UserBelongsToHouseholdAsync(userId, householdId, cancellationToken)) return null;

        var symbol = request.Symbol.Trim().ToUpperInvariant();
        var providerSymbol = string.IsNullOrWhiteSpace(request.ProviderSymbol)
            ? YahooSymbolMap.ToYahoo(symbol, request.MicCode.Trim())
            : request.ProviderSymbol.Trim();

        var holding = await _investmentRepository.GetByProviderSymbolAsync(householdId, providerSymbol, cancellationToken);
        if (holding == null)
        {
            holding = new InvestmentHolding
            {
                Id = Guid.NewGuid(),
                Symbol = symbol,
                Exchange = request.Exchange.Trim(),
                ProviderSymbol = providerSymbol,
                Name = request.Name.Trim(),
                LogoDomain = string.IsNullOrWhiteSpace(request.LogoDomain) ? null : request.LogoDomain.Trim(),
                Currency = string.IsNullOrWhiteSpace(request.Currency) ? "EUR" : request.Currency.Trim().ToUpperInvariant(),
                Type = request.Type,
                HouseholdId = householdId,
                CreatedAt = DateTime.UtcNow
            };
            await _investmentRepository.CreateAsync(holding, cancellationToken);
        }

        var txDate = ToUtc(request.Date);
        var isEur = string.Equals(holding.Currency, "EUR", StringComparison.OrdinalIgnoreCase);
        var fxRate = isEur ? 1m : await _fxRateService.GetRateToEurAsync(holding.Currency, txDate, cancellationToken);

        await _investmentRepository.AddTransactionAsync(new InvestmentTransaction
        {
            Id = Guid.NewGuid(),
            InvestmentHoldingId = holding.Id,
            Operation = request.Operation,
            Date = txDate,
            Quantity = request.Quantity,
            UnitPrice = request.UnitPrice,
            Commission = request.Commission,
            FxRateToEur = fxRate,
            FxFeePercent = isEur ? 0m : request.FxFeePercent,
            CreatedAt = DateTime.UtcNow
        }, cancellationToken);

        // Best-effort: vai buscar já a cotação para o utilizador ver valor de imediato.
        try { await _refreshService.RefreshSymbolsAsync(new[] { providerSymbol }, cancellationToken); }
        catch { /* o job diário recupera */ }

        var refreshed = await _investmentRepository.GetByIdAsync(holding.Id, cancellationToken);
        return refreshed == null ? null : await BuildDtoAsync(refreshed, cancellationToken);
    }

    public async Task<InvestmentHoldingDto?> UpdateTransactionAsync(Guid transactionId, UpdateTransactionRequest request, Guid userId, CancellationToken cancellationToken = default)
    {
        var tx = await _investmentRepository.GetTransactionByIdAsync(transactionId, cancellationToken);
        if (tx == null) return null;
        if (!await UserBelongsToHouseholdAsync(userId, tx.InvestmentHolding.HouseholdId, cancellationToken)) return null;

        var ccy = tx.InvestmentHolding.Currency;
        var isEur = string.Equals(ccy, "EUR", StringComparison.OrdinalIgnoreCase);
        var txDate = ToUtc(request.Date);

        tx.Operation = request.Operation;
        tx.Date = txDate;
        tx.Quantity = request.Quantity;
        tx.UnitPrice = request.UnitPrice;
        tx.Commission = request.Commission;
        tx.FxRateToEur = isEur ? 1m : await _fxRateService.GetRateToEurAsync(ccy, txDate, cancellationToken);
        tx.FxFeePercent = isEur ? 0m : request.FxFeePercent;
        tx.UpdatedAt = DateTime.UtcNow;
        await _investmentRepository.UpdateTransactionAsync(tx, cancellationToken);

        var holding = await _investmentRepository.GetByIdAsync(tx.InvestmentHoldingId, cancellationToken);
        return holding == null ? null : await BuildDtoAsync(holding, cancellationToken);
    }

    public async Task<InvestmentHoldingDto?> DeleteTransactionAsync(Guid transactionId, Guid userId, CancellationToken cancellationToken = default)
    {
        var tx = await _investmentRepository.GetTransactionByIdAsync(transactionId, cancellationToken);
        if (tx == null) return null;
        if (!await UserBelongsToHouseholdAsync(userId, tx.InvestmentHolding.HouseholdId, cancellationToken)) return null;

        var holdingId = tx.InvestmentHoldingId;
        await _investmentRepository.DeleteTransactionAsync(tx, cancellationToken);

        var holding = await _investmentRepository.GetByIdAsync(holdingId, cancellationToken);
        if (holding == null) return null;

        // Posição sem transações deixa de existir.
        if (holding.Transactions.Count == 0)
        {
            await _investmentRepository.DeleteAsync(holding, cancellationToken);
            return null;
        }
        return await BuildDtoAsync(holding, cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var holding = await _investmentRepository.GetByIdAsync(id, cancellationToken);
        if (holding == null) return false;
        if (!await UserBelongsToHouseholdAsync(userId, holding.HouseholdId, cancellationToken)) return false;
        await _investmentRepository.DeleteAsync(holding, cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<InstrumentSearchResultDto>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        var results = await _marketDataProvider.SearchAsync(query, cancellationToken);

        // Domínio da marca via Logo.dev (só ações; ETFs usam o mapa de emissores no frontend).
        // Dedup por nome para minimizar chamadas.
        var domainByName = new System.Collections.Concurrent.ConcurrentDictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var stockNames = results
            .Where(r => r.Type == InstrumentType.Stock && !string.IsNullOrWhiteSpace(r.Name))
            .Select(r => r.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        await Task.WhenAll(stockNames.Select(async name =>
            domainByName[name] = await _marketDataProvider.ResolveBrandDomainAsync(CleanCompanyName(name), cancellationToken)));

        return results.Select(r => new InstrumentSearchResultDto
        {
            Symbol = r.Symbol,
            Name = r.Name,
            Exchange = r.Exchange,
            MicCode = r.MicCode,
            Currency = r.Currency,
            Type = r.Type,
            ProviderSymbol = r.ProviderSymbol,
            LogoDomain = r.Type == InstrumentType.Stock && domainByName.TryGetValue(r.Name, out var d) ? d : null
        }).ToList();
    }

    /// <summary>Limpa o nome para a pesquisa de marca (remove "(TICKER)" e formas jurídicas comuns).</summary>
    private static string CleanCompanyName(string name)
    {
        var n = name;
        var p = n.IndexOf('(');
        if (p > 0) n = n[..p];
        return n.Trim();
    }

    // Cooldown server-side do refresh manual por agregado: o botão do cliente já trava 10 min, mas isto
    // fecha o bypass (limpar localStorage / outro dispositivo) e evita martelar o Yahoo. Em memória.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, DateTime> _lastRefreshByHousehold = new();
    private static readonly TimeSpan RefreshCooldown = TimeSpan.FromSeconds(60);

    public async Task<IReadOnlyList<InvestmentHoldingDto>> RefreshHouseholdQuotesAsync(Guid householdId, Guid userId, CancellationToken cancellationToken = default)
    {
        if (!await UserBelongsToHouseholdAsync(userId, householdId, cancellationToken))
            return Array.Empty<InvestmentHoldingDto>();

        var now = DateTime.UtcNow;
        if (!_lastRefreshByHousehold.TryGetValue(householdId, out var last) || now - last >= RefreshCooldown)
        {
            var holdings = await _investmentRepository.GetByHouseholdIdAsync(householdId, cancellationToken);
            await _refreshService.RefreshSymbolsAsync(holdings.Select(h => h.ProviderSymbol), cancellationToken);
            _lastRefreshByHousehold[householdId] = now;
        }
        // Dentro do cooldown devolve na mesma os DTOs atuais (cotações da BD) — sem chamada externa.
        return await GetByHouseholdAsync(householdId, userId, cancellationToken);
    }

    public async Task<InvestmentHistoryDto> GetHouseholdHistoryAsync(Guid householdId, Guid userId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
    {
        if (!await UserBelongsToHouseholdAsync(userId, householdId, cancellationToken)) return new InvestmentHistoryDto();
        var holdings = await _investmentRepository.GetByHouseholdIdAsync(householdId, cancellationToken);
        return await BuildHistoryAsync(holdings, from, to, cancellationToken);
    }

    public async Task<InvestmentHistoryDto?> GetHoldingHistoryAsync(Guid holdingId, Guid userId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
    {
        var holding = await _investmentRepository.GetByIdAsync(holdingId, cancellationToken);
        if (holding == null) return null;
        if (!await UserBelongsToHouseholdAsync(userId, holding.HouseholdId, cancellationToken)) return null;
        return await BuildHistoryAsync(new[] { holding }, from, to, cancellationToken);
    }

    public async Task<InstrumentPriceHistoryDto> GetInstrumentHistoryAsync(string providerSymbol, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var dto = new InstrumentPriceHistoryDto();
        if (string.IsNullOrWhiteSpace(providerSymbol)) return dto;

        var prices = await _marketDataProvider.GetHistoryAsync(providerSymbol.Trim(), from, to, cancellationToken);
        dto.Points = prices
            .Select(p => new InstrumentPricePointDto { Date = p.Date.ToString("yyyy-MM-dd"), Value = p.Close })
            .ToList();
        return dto;
    }

    /// <summary>Estado pré-calculado de uma posição para amostrar o valor a qualquer data.</summary>
    private sealed class HoldingSeries
    {
        public List<(DateOnly Date, decimal Qty, decimal InvestedEur)> Steps = new();
        public IReadOnlyList<PricePoint> Prices = Array.Empty<PricePoint>();
        public List<(DateOnly Date, decimal Rate)> Fx = new();
        public decimal FxFallback = 1m;
        public decimal CurrentInvestedEur;
        public decimal? CurrentValueEur;
    }

    private async Task<InvestmentHistoryDto> BuildHistoryAsync(IReadOnlyList<InvestmentHolding> holdings, DateOnly? fromOpt, DateOnly? toOpt, CancellationToken cancellationToken)
    {
        var result = new InvestmentHistoryDto();
        var withTx = holdings.Where(h => h.Transactions.Count > 0).ToList();
        if (withTx.Count == 0) return result;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var earliest = withTx.SelectMany(h => h.Transactions).Min(t => DateOnly.FromDateTime(t.Date));
        // Período fixo (ex.: 5A) → mostra o intervalo completo, mesmo a 0 antes da 1ª compra (igual ao património).
        // "Tudo" (from vazio) → começa na 1ª compra.
        var from = fromOpt ?? earliest;
        var to = toOpt ?? today;
        if (to > today) to = today;
        if (to < from) to = from;

        var rates = await _fxRateService.GetRatesToEurAsync(cancellationToken);
        var quotes = await GetQuoteMapAsync(withTx.Select(h => h.ProviderSymbol), cancellationToken);

        // Buscar histórico (por SÍMBOLO) e séries de câmbio (por MOEDA) uma só vez cada, em paralelo —
        // evita N+1 quando várias posições partilham símbolo/moeda (ex.: várias em USD).
        var symbols = withTx.Select(h => h.ProviderSymbol)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var currencies = withTx.Select(h => h.Currency)
            .Where(c => !string.IsNullOrWhiteSpace(c) && !string.Equals(c, "EUR", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var priceTasks = symbols.ToDictionary(
            s => s,
            s => _marketDataProvider.GetHistoryAsync(s, from, to, cancellationToken),
            StringComparer.OrdinalIgnoreCase);
        var fxTasks = currencies.ToDictionary(
            c => c,
            c => _fxRateService.GetRateSeriesToEurAsync(c, from, to, cancellationToken),
            StringComparer.OrdinalIgnoreCase);

        await Task.WhenAll(priceTasks.Values.Cast<Task>().Concat(fxTasks.Values));

        var emptyFx = new Dictionary<DateOnly, decimal>();
        var prepared = new List<HoldingSeries>();
        foreach (var h in withTx)
        {
            var prices = priceTasks.TryGetValue(h.ProviderSymbol, out var pt) ? pt.Result : (IReadOnlyList<PricePoint>)Array.Empty<PricePoint>();
            IReadOnlyDictionary<DateOnly, decimal> fxMap = fxTasks.TryGetValue(h.Currency, out var ft) ? ft.Result : emptyFx;
            prepared.Add(BuildHoldingSeries(h, prices, fxMap, rates, quotes.GetValueOrDefault(h.ProviderSymbol)));
        }

        var totalDays = to.DayNumber - from.DayNumber;
        var step = Math.Max(1, (int)Math.Ceiling((totalDays + 1) / 370.0));

        var points = new List<InvestmentHistoryPointDto>();
        for (var dn = from.DayNumber; dn <= to.DayNumber; dn += step)
            points.Add(AggregateAt(prepared, DateOnly.FromDayNumber(dn)));

        if (points.Count == 0 || points[^1].Date != to.ToString("yyyy-MM-dd"))
            points.Add(AggregateAt(prepared, to));

        // Âncora: o último ponto reflete exatamente o valor/custo atuais (consistência com o hero e a tabela).
        decimal anchorValue = 0m, anchorCost = 0m;
        foreach (var s in prepared)
        {
            anchorCost += s.CurrentInvestedEur;
            anchorValue += s.CurrentValueEur ?? s.CurrentInvestedEur;
        }
        points[^1].Value = anchorValue;
        points[^1].Cost = anchorCost;

        result.Points = points;
        return result;
    }

    private static HoldingSeries BuildHoldingSeries(InvestmentHolding h, IReadOnlyList<PricePoint> prices, IReadOnlyDictionary<DateOnly, decimal> fxMap, IReadOnlyDictionary<string, decimal> rates, InstrumentQuote? quote)
    {
        var s = new HoldingSeries { Prices = prices };

        // Passos de quantidade/custo acumulados, por transação (ordenadas por data).
        decimal qty = 0m, avgCostEur = 0m;
        foreach (var t in h.Transactions.OrderBy(t => t.Date).ThenBy(t => t.CreatedAt))
        {
            if (t.Operation == InvestmentOperation.Buy)
            {
                var costEur = (t.Quantity * t.UnitPrice + t.Commission) * t.FxRateToEur * (1m + t.FxFeePercent / 100m);
                var newQty = qty + t.Quantity;
                avgCostEur = newQty != 0 ? (qty * avgCostEur + costEur) / newQty : 0m;
                qty = newQty;
            }
            else
            {
                qty -= t.Quantity;
                if (qty < 0) qty = 0m;
            }
            s.Steps.Add((DateOnly.FromDateTime(t.Date), qty, qty * avgCostEur));
        }

        // Série de câmbio ordenada + fallback (taxa de hoje para a moeda).
        s.Fx = fxMap.Select(kv => (kv.Key, kv.Value)).OrderBy(x => x.Key).ToList();
        s.FxFallback = string.Equals(h.Currency, "EUR", StringComparison.OrdinalIgnoreCase)
            ? 1m
            : (rates.TryGetValue(h.Currency, out var r) ? r : 1m);

        s.CurrentInvestedEur = qty * avgCostEur; // = NetQuantity × custo médio (EUR)
        if (quote != null)
        {
            var quoteRate = rates.TryGetValue(quote.Currency, out var qr) ? qr : 1m;
            s.CurrentValueEur = h.NetQuantity * quote.Price * quoteRate;
        }
        return s;
    }

    private static InvestmentHistoryPointDto AggregateAt(IReadOnlyList<HoldingSeries> series, DateOnly d)
    {
        decimal value = 0m, cost = 0m;
        foreach (var s in series)
        {
            // Estado (qty, investido) à data: último passo com Date <= d.
            decimal qty = 0m, investedEur = 0m;
            foreach (var st in s.Steps)
            {
                if (st.Date <= d) { qty = st.Qty; investedEur = st.InvestedEur; }
                else break;
            }
            if (qty <= 0m) continue;

            cost += investedEur;

            var price = PriceAsOf(s.Prices, d);
            if (price == null) { value += investedEur; continue; } // sem preço → mostra o custo

            var fx = FxAsOf(s.Fx, d, s.FxFallback);
            value += qty * price.Value * fx;
        }
        return new InvestmentHistoryPointDto { Date = d.ToString("yyyy-MM-dd"), Value = value, Cost = cost };
    }

    private static decimal? PriceAsOf(IReadOnlyList<PricePoint> prices, DateOnly d)
    {
        if (prices.Count == 0) return null;
        decimal? last = null;
        foreach (var p in prices)
        {
            if (p.Date <= d) last = p.Close;
            else break;
        }
        // Antes do 1º fecho disponível, usa o 1º (evita um degrau a 0 no início).
        return last ?? prices[0].Close;
    }

    private static decimal FxAsOf(IReadOnlyList<(DateOnly Date, decimal Rate)> fx, DateOnly d, decimal fallback)
    {
        if (fx.Count == 0) return fallback;
        decimal? last = null;
        foreach (var p in fx)
        {
            if (p.Date <= d) last = p.Rate;
            else break;
        }
        return last ?? fx[0].Rate;
    }

    public async Task<InvestmentImportResultDto> ImportTradesAsync(BrokerImportRequest request, Guid householdId, Guid userId, bool dryRun, CancellationToken cancellationToken = default)
    {
        var result = new InvestmentImportResultDto { DryRun = dryRun, HasUnparsedRows = request.HasUnparsedRows };
        if (!await UserBelongsToHouseholdAsync(userId, householdId, cancellationToken))
        {
            result.Error = "Sem acesso a este agregado.";
            return result;
        }

        var items = request.Items ?? new List<BrokerTradeDto>();
        if (items.Count == 0)
        {
            result.Error = "Não foram encontradas transações para importar.";
            return result;
        }
        result.Detected = items.Count;

        // IDs já existentes neste agregado (para ignorar duplicados em reimportações).
        var existingHoldings = await _investmentRepository.GetByHouseholdIdAsync(householdId, cancellationToken);
        var seenExternalIds = new HashSet<string>(
            existingHoldings.SelectMany(h => h.Transactions)
                .Select(t => t.ExternalId)
                .Where(id => !string.IsNullOrEmpty(id))!,
            StringComparer.Ordinal);

        var holdingCache = new Dictionary<string, InvestmentHolding>(StringComparer.OrdinalIgnoreCase);
        var touchedSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Ordena por data para criar compras antes das vendas.
        var ordered = items
            .Select(t => (Trade: t, Date: ParseDateOnly(t.Date)))
            .OrderBy(x => x.Date)
            .ToList();

        foreach (var (trade, date) in ordered)
        {
            var externalId = trade.ExternalId?.Trim() ?? string.Empty;
            var isDuplicate = string.IsNullOrEmpty(externalId) ? false : !seenExternalIds.Add(externalId);

            result.Items.Add(new InvestmentImportItemDto
            {
                ProviderSymbol = trade.ProviderSymbol,
                Name = string.IsNullOrWhiteSpace(trade.Name) ? trade.BaseSymbol : trade.Name,
                Operation = trade.Operation == InvestmentOperation.Buy ? "Compra" : "Venda",
                Date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Quantity = trade.Quantity,
                UnitPrice = trade.UnitPrice,
                Currency = trade.Currency,
                Status = isDuplicate ? "duplicate" : "new",
            });

            if (isDuplicate) { result.Skipped++; continue; }
            result.Created++;

            if (dryRun) continue;

            var holding = await GetOrCreateHoldingForImportAsync(holdingCache, householdId, trade, cancellationToken);
            var isEur = string.Equals(trade.Currency, "EUR", StringComparison.OrdinalIgnoreCase);
            var fx = isEur ? 1m
                : (trade.FxRateToEur is > 0 ? trade.FxRateToEur.Value
                    : await _fxRateService.GetRateToEurAsync(trade.Currency, date, cancellationToken));

            await _investmentRepository.AddTransactionAsync(new InvestmentTransaction
            {
                Id = Guid.NewGuid(),
                InvestmentHoldingId = holding.Id,
                Operation = trade.Operation,
                Date = date,
                Quantity = trade.Quantity,
                UnitPrice = trade.UnitPrice,
                Commission = 0m,
                FxRateToEur = fx,
                FxFeePercent = 0m,
                ExternalId = string.IsNullOrEmpty(externalId) ? null : externalId,
                CreatedAt = DateTime.UtcNow,
            }, cancellationToken);

            // Usa o símbolo REAL da posição (já resolvido do ISIN), não o do extrato — senão o Yahoo não cota.
            touchedSymbols.Add(holding.ProviderSymbol);
        }

        if (!dryRun && touchedSymbols.Count > 0)
        {
            try { await _refreshService.RefreshSymbolsAsync(touchedSymbols, cancellationToken); }
            catch { /* o job diário recupera */ }
        }

        return result;
    }

    private static DateTime ParseDateOnly(string date)
    {
        if (DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d))
            return DateTime.SpecifyKind(d.Date, DateTimeKind.Utc);
        return DateTime.UtcNow.Date;
    }

    private readonly Dictionary<string, InstrumentSearchResult?> _isinResolveCache = new(StringComparer.OrdinalIgnoreCase);

    private static bool LooksLikeIsin(string s)
        => s.Length == 12 && char.IsLetter(s[0]) && char.IsLetter(s[1]) && char.IsDigit(s[11]);

    /// <summary>Resolve um ISIN → instrumento cotável (Twelve Data aceita ISIN). Best-effort, com cache por importação.</summary>
    private async Task<InstrumentSearchResult?> ResolveIsinAsync(string isin, CancellationToken cancellationToken)
    {
        if (_isinResolveCache.TryGetValue(isin, out var cached)) return cached;
        InstrumentSearchResult? result = null;
        try
        {
            var results = await _marketDataProvider.SearchAsync(isin, cancellationToken);
            result = results.Count > 0 ? results[0] : null;
        }
        catch { /* rate-limit / falha → fica como ISIN */ }
        _isinResolveCache[isin] = result;
        return result;
    }

    private async Task<InvestmentHolding> GetOrCreateHoldingForImportAsync(
        Dictionary<string, InvestmentHolding> cache, Guid householdId, BrokerTradeDto trade, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(trade.ProviderSymbol, out var cached)) return cached;

        // Sem ticker real (providerSymbol é um ISIN) → tenta resolver para um símbolo cotável + nome.
        var providerSymbol = trade.ProviderSymbol;
        var baseSymbol = trade.BaseSymbol;
        var name = string.IsNullOrWhiteSpace(trade.Name) ? trade.BaseSymbol : trade.Name;
        var exchange = trade.Exchange;
        var type = trade.Type;
        var isinForLookup = !string.IsNullOrWhiteSpace(trade.Isin) ? trade.Isin! : (LooksLikeIsin(providerSymbol) ? providerSymbol : null);

        if (LooksLikeIsin(providerSymbol) && isinForLookup != null)
        {
            var r = await ResolveIsinAsync(isinForLookup, cancellationToken);
            if (r != null && !string.IsNullOrWhiteSpace(r.ProviderSymbol))
            {
                providerSymbol = r.ProviderSymbol;
                baseSymbol = string.IsNullOrWhiteSpace(r.Symbol) ? baseSymbol : r.Symbol;
                name = string.IsNullOrWhiteSpace(r.Name) ? name : r.Name;
                exchange = string.IsNullOrWhiteSpace(r.Exchange) ? exchange : r.Exchange;
                type = r.Type;
                // Moeda mantém-se a do extrato (o preço importado está nessa moeda); a conversão
                // do valor de mercado usa a moeda da cotação do Yahoo, por isso os totais em € batem certo.
            }
        }

        var holding = await _investmentRepository.GetByProviderSymbolAsync(householdId, providerSymbol, cancellationToken);
        if (holding == null)
        {
            holding = new InvestmentHolding
            {
                Id = Guid.NewGuid(),
                Symbol = baseSymbol,
                Exchange = exchange,
                ProviderSymbol = providerSymbol,
                Name = name,
                LogoDomain = null,
                Currency = string.IsNullOrWhiteSpace(trade.Currency) ? "EUR" : trade.Currency.ToUpperInvariant(),
                Type = type,
                HouseholdId = householdId,
                CreatedAt = DateTime.UtcNow,
            };
            await _investmentRepository.CreateAsync(holding, cancellationToken);
        }
        cache[trade.ProviderSymbol] = holding;
        return holding;
    }

    private async Task<InvestmentHoldingDto> BuildDtoAsync(InvestmentHolding holding, CancellationToken cancellationToken)
    {
        var quotes = await GetQuoteMapAsync(new[] { holding.ProviderSymbol }, cancellationToken);
        var rates = await _fxRateService.GetRatesToEurAsync(cancellationToken);
        return ToDto(holding, quotes.GetValueOrDefault(holding.ProviderSymbol), rates);
    }

    private async Task<Dictionary<string, InstrumentQuote>> GetQuoteMapAsync(IEnumerable<string> symbols, CancellationToken cancellationToken)
    {
        var quotes = await _quoteRepository.GetBySymbolsAsync(symbols, cancellationToken);
        return quotes.ToDictionary(q => q.ProviderSymbol, q => q);
    }

    private async Task<bool> UserBelongsToHouseholdAsync(Guid userId, Guid householdId, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        return user != null && user.HouseholdId.HasValue && user.HouseholdId.Value == householdId;
    }

    private static DateTime ToUtc(DateTime value)
        => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static InvestmentHoldingDto ToDto(InvestmentHolding h, InstrumentQuote? quote, IReadOnlyDictionary<string, decimal> rates)
    {
        decimal RateOf(string ccy) => rates.TryGetValue(ccy, out var r) ? r : 1m;

        var netQty = h.NetQuantity;
        var avgCost = h.AverageCost; // na moeda do instrumento, para apresentação

        // Investido em EUR: cada compra é convertida à taxa do seu dia × (1 + fee do broker).
        decimal buyCostEur = 0m, buyQty = 0m;
        foreach (var t in h.Transactions)
        {
            if (t.Operation != InvestmentOperation.Buy) continue;
            var costCcy = t.Quantity * t.UnitPrice + t.Commission;
            buyCostEur += costCcy * t.FxRateToEur * (1m + t.FxFeePercent / 100m);
            buyQty += t.Quantity;
        }
        var avgCostEur = buyQty > 0 ? buyCostEur / buyQty : 0m;
        var investedEur = netQty * avgCostEur;

        decimal? currentPrice = quote?.Price;
        decimal? currentValueEur = null;
        decimal? returnEur = null;
        decimal? returnPct = null;

        if (quote != null)
        {
            currentValueEur = netQty * quote.Price * RateOf(quote.Currency);
            returnEur = currentValueEur - investedEur;
            returnPct = investedEur != 0 ? returnEur / Math.Abs(investedEur) * 100m : null;
        }

        return new InvestmentHoldingDto
        {
            Id = h.Id,
            HouseholdId = h.HouseholdId,
            Symbol = h.Symbol,
            Exchange = h.Exchange,
            ProviderSymbol = h.ProviderSymbol,
            Name = h.Name,
            Currency = h.Currency,
            Type = h.Type,
            LogoDomain = h.LogoDomain,
            Quantity = netQty,
            AverageCost = avgCost,
            CurrentPrice = currentPrice,
            PriceAsOf = quote?.AsOf,
            InvestedEur = investedEur,
            CurrentValueEur = currentValueEur,
            ReturnEur = returnEur,
            ReturnPct = returnPct,
            Transactions = h.Transactions
                .OrderByDescending(t => t.Date)
                .ThenByDescending(t => t.CreatedAt)
                .Select(t => new InvestmentTransactionDto
                {
                    Id = t.Id,
                    Operation = t.Operation,
                    Date = t.Date,
                    Quantity = t.Quantity,
                    UnitPrice = t.UnitPrice,
                    Commission = t.Commission,
                    FxFeePercent = t.FxFeePercent
                }).ToList()
        };
    }
}
