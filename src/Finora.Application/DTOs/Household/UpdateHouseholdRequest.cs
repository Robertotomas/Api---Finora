using System.ComponentModel.DataAnnotations;
using Finora.Domain.Enums;

namespace Finora.Application.DTOs.Household;

public record UpdateHouseholdRequest
{
    [Required]
    public HouseholdType Type { get; init; }

    [Required]
    [MaxLength(200)]
    public string Name { get; init; } = string.Empty;
}
