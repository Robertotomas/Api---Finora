namespace Finora.Application.Options;

/// <summary>
/// Config dos dados de mercado. A pesquisa de símbolos usa a Twelve Data (precisa de ApiKey);
/// os preços/histórico vêm do Yahoo (não-oficial, sem chave) porque a Twelve Data gratuita não cobre EU.
/// Segredos vêm de env (MarketData__ApiKey) / appsettings.Local.json.
/// </summary>
public class MarketDataOptions
{
    public const string SectionName = "MarketData";

    public string Provider { get; set; } = "TwelveData";

    /// <summary>Chave da Twelve Data (pesquisa de símbolos).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Base URL da Twelve Data.</summary>
    public string BaseUrl { get; set; } = "https://api.twelvedata.com";

    /// <summary>Base URL do Yahoo (preços/histórico).</summary>
    public string YahooBaseUrl { get; set; } = "https://query1.finance.yahoo.com";

    /// <summary>Base URL do serviço de câmbio (BCE via frankfurter).</summary>
    public string FxBaseUrl { get; set; } = "https://api.frankfurter.app";
}
