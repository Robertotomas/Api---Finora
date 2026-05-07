using System.ComponentModel.DataAnnotations;

namespace Finora.Application.DTOs.Auth;

public record RefreshTokenRequest
{
    [Required]
    public string RefreshToken { get; init; } = string.Empty;
}
