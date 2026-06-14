using Finora.Domain.Common;

namespace Finora.Domain.Entities;

/// <summary>Uma avaliação (snapshot de valor) de um <see cref="Asset"/> numa dada data.</summary>
public class AssetValuation : BaseEntity
{
    public Guid AssetId { get; set; }
    public Asset Asset { get; set; } = null!;

    public DateTime Date { get; set; }
    public decimal Value { get; set; }
}
