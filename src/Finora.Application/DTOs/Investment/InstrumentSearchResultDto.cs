using Finora.Domain.Enums;

namespace Finora.Application.DTOs.Investment;

public record InstrumentSearchResultDto
{
    public string Symbol { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Exchange { get; init; } = string.Empty;
    public string MicCode { get; init; } = string.Empty;
    public string Currency { get; init; } = "EUR";
    public InstrumentType Type { get; init; }
    /// <summary>Símbolo Yahoo resolvido (usado para os preços). Pode vir vazio se a bolsa não for mapeável.</summary>
    public string ProviderSymbol { get; init; } = string.Empty;

    /// <summary>Domínio da marca para o logo (ex.: "adidas.com"), resolvido via Logo.dev. Pode ser vazio.</summary>
    public string? LogoDomain { get; init; }
}
