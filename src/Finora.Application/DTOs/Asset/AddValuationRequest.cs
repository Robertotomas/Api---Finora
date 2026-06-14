using System.ComponentModel.DataAnnotations;

namespace Finora.Application.DTOs.Asset;

public record AddValuationRequest
{
    [Required]
    public DateTime Date { get; init; }

    public decimal Value { get; init; }
}
