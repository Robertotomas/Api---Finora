using System.ComponentModel.DataAnnotations;
using Finora.Domain.Enums;

namespace Finora.Application.DTOs.Auth;

public record UpdateProfileRequest
{
    [Required]
    [MaxLength(100)]
    public string FirstName { get; init; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string LastName { get; init; } = string.Empty;

    public Gender? Gender { get; init; }

    /// <summary>Optional IANA timezone (e.g. Europe/Lisbon).</summary>
    [MaxLength(100)]
    public string? TimeZoneId { get; init; }
}
