using System.ComponentModel.DataAnnotations;
using Finora.Domain.Enums;

namespace Finora.Application.DTOs.Asset;

public record CreateAssetRequest
{
    [Required]
    [MaxLength(200)]
    [MinLength(1, ErrorMessage = "O nome deve ter pelo menos 1 caractere.")]
    public string Name { get; init; } = string.Empty;

    [Required]
    public AssetCategory Category { get; init; }

    public decimal AcquisitionCost { get; init; }

    [Required]
    public DateTime AcquisitionDate { get; init; }
}
