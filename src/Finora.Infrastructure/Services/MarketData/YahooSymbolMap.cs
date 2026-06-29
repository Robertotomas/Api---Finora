namespace Finora.Infrastructure.Services.MarketData;

/// <summary>
/// Converte (símbolo Twelve Data + MIC da bolsa) no símbolo do Yahoo usado para ir buscar o preço.
/// Ex.: ("VWCE", "XETR") → "VWCE.DE"; ("AAPL", "XNGS") → "AAPL".
/// </summary>
public static class YahooSymbolMap
{
    // Bolsas dos EUA (Yahoo usa o símbolo sem sufixo).
    private static readonly HashSet<string> UsMics = new(StringComparer.OrdinalIgnoreCase)
    {
        "XNAS", "XNGS", "XNMS", "XNCM", "XNYS", "ARCX", "BATS", "XASE", "XBOS", "IEXG", "OTCM", "PINX"
    };

    // MIC → sufixo Yahoo (bolsas europeias e outras comuns).
    private static readonly Dictionary<string, string> MicToSuffix = new(StringComparer.OrdinalIgnoreCase)
    {
        ["XETR"] = ".DE", // Xetra
        ["XFRA"] = ".F",  // Frankfurt
        ["XBER"] = ".BE", // Berlim
        ["XDUS"] = ".DU", // Düsseldorf
        ["XHAM"] = ".HM", // Hamburgo
        ["XMUN"] = ".MU", // Munique
        ["XSTU"] = ".SG", // Estugarda
        ["XMIL"] = ".MI", // Milão (Borsa Italiana)
        ["MTAA"] = ".MI",
        ["XAMS"] = ".AS", // Euronext Amesterdão
        ["XPAR"] = ".PA", // Euronext Paris
        ["XBRU"] = ".BR", // Euronext Bruxelas
        ["XLIS"] = ".LS", // Euronext Lisboa
        ["XMAD"] = ".MC", // Madrid (BME)
        ["XLON"] = ".L",  // London Stock Exchange
        ["BCXE"] = ".L",  // Cboe Europe (Reino Unido)
        ["XSWX"] = ".SW", // SIX Swiss
        ["XVTX"] = ".SW",
        ["XWBO"] = ".VI", // Viena
        ["XHEL"] = ".HE", // Helsínquia
        ["XSTO"] = ".ST", // Estocolmo
        ["XCSE"] = ".CO", // Copenhaga
        ["XOSL"] = ".OL", // Oslo
        ["XIST"] = ".IS", // Istambul
        ["XWAR"] = ".WA", // Varsóvia
        ["XTSE"] = ".TO", // Toronto
        ["XHKG"] = ".HK", // Hong Kong
        ["XTKS"] = ".T",  // Tóquio
    };

    public static string ToYahoo(string symbol, string micCode)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return string.Empty;
        var sym = symbol.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(micCode) || UsMics.Contains(micCode))
            return sym;
        return MicToSuffix.TryGetValue(micCode, out var suffix) ? sym + suffix : sym;
    }

    // Sufixos de bolsa conhecidos (sem ponto) — inverso de MicToSuffix.
    private static readonly HashSet<string> KnownSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "DE", "F", "BE", "DU", "HM", "MU", "SG", "MI", "AS", "PA", "BR", "LS",
        "MC", "L", "SW", "VI", "HE", "ST", "CO", "OL", "IS", "WA", "TO", "HK", "T"
    };

    /// <summary>
    /// Remove um sufixo de bolsa estilo Yahoo (ex.: "VUAA.DE" → "VUAA"), porque a pesquisa da
    /// Twelve Data usa o símbolo base. Não toca em pontos legítimos (ex.: classes "BRK.B").
    /// </summary>
    public static string StripExchangeSuffix(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return query;
        var q = query.Trim();
        var i = q.LastIndexOf('.');
        if (i > 0 && i < q.Length - 1 && KnownSuffixes.Contains(q[(i + 1)..]))
            return q[..i];
        return q;
    }
}
