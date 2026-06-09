using System.ComponentModel.DataAnnotations;

namespace Finora.Application.DTOs.Auth;

public record ForgotPasswordRequest
{
    [Required]
    [EmailAddress]
    [MaxLength(256)]
    public string Email { get; init; } = string.Empty;
}
