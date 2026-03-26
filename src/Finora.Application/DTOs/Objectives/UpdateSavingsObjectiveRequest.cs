using System.ComponentModel.DataAnnotations;

namespace Finora.Application.DTOs.Objectives;

public record UpdateSavingsObjectiveRequest
{
    [Required]
    [MaxLength(200)]
    public string Name { get; init; } = string.Empty;

    [Range(typeof(decimal), "0.01", "999999999999.99", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public decimal TargetAmount { get; init; }

    public DateOnly? TargetDate { get; init; }
}
