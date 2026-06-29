namespace Finora.Application.Options;

/// <summary>
/// Logo.dev brand search (resolve nome de empresa → domínio, para o logo correto).
/// A SecretKey (sk_…) é usada só no servidor (nunca exposta ao cliente). O frontend usa a
/// publishable (pk_) só para mostrar o logo a partir do domínio.
/// </summary>
public class LogoDevOptions
{
    public const string SectionName = "LogoDev";

    /// <summary>Chave secreta (sk_…) para a API de pesquisa de marcas.</summary>
    public string SecretKey { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "https://api.logo.dev";
}
