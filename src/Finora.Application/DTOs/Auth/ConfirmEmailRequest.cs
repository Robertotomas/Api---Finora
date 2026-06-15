using System.ComponentModel.DataAnnotations;

namespace Finora.Application.DTOs.Auth;

public record ConfirmEmailRequest
{
    [Required]
    public string Token { get; init; } = string.Empty;
}
