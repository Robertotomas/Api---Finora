using Finora.Domain.Common;
using Finora.Domain.Enums;

namespace Finora.Domain.Entities;

/// <summary>
/// Um bem/ativo do agregado (imóvel, arte, veículo...). O seu valor atual (avaliação mais recente)
/// conta apenas para o Património Total — não mexe em receitas/despesas, contas ou objetivos.
/// </summary>
public class Asset : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public AssetCategory Category { get; set; }

    /// <summary>Custo de aquisição (valor pago). Vira a avaliação base na data de aquisição.</summary>
    public decimal AcquisitionCost { get; set; }
    public string Currency { get; set; } = "EUR";
    public DateTime AcquisitionDate { get; set; }

    public Guid HouseholdId { get; set; }
    public Household Household { get; set; } = null!;

    public ICollection<AssetValuation> Valuations { get; set; } = new List<AssetValuation>();

    /// <summary>Avaliação mais recente (por data). Fallback para o custo de aquisição se não houver avaliações.</summary>
    public decimal CurrentValue => LatestValuation?.Value ?? AcquisitionCost;

    /// <summary>Data da avaliação mais recente, ou a data de aquisição se não houver avaliações.</summary>
    public DateTime LastValuationDate => LatestValuation?.Date ?? AcquisitionDate;

    private AssetValuation? LatestValuation =>
        Valuations is { Count: > 0 }
            ? Valuations.OrderByDescending(v => v.Date).ThenByDescending(v => v.CreatedAt).First()
            : null;
}
