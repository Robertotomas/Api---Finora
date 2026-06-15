using Finora.Domain.Enums;

namespace Finora.Application.DTOs.Asset;

public record AssetDto
{
    public Guid Id { get; init; }
    public Guid HouseholdId { get; init; }
    public string Name { get; init; } = string.Empty;
    public AssetCategory Category { get; init; }
    public decimal AcquisitionCost { get; init; }
    public string Currency { get; init; } = "EUR";
    public DateTime AcquisitionDate { get; init; }

    /// <summary>Valor atual = avaliação mais recente (fallback custo de aquisição).</summary>
    public decimal CurrentValue { get; init; }
    public DateTime LastValuationDate { get; init; }

    /// <summary>Histórico de avaliações, ordenado por data desc.</summary>
    public IReadOnlyList<AssetValuationDto> Valuations { get; init; } = Array.Empty<AssetValuationDto>();
}
