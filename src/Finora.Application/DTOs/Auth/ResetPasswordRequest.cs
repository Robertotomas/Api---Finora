using System.ComponentModel.DataAnnotations;

namespace Finora.Application.DTOs.Auth;

public record ResetPasswordRequest
{
    [Required]
    public string Token { get; init; } = string.Empty;

    [Required]
    [MinLength(8, ErrorMessage = "A password deve ter pelo menos 8 caracteres.")]
    [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).{8,}$",
        ErrorMessage = "A password deve conter maiúscula, minúscula e número.")]
    public string NewPassword { get; init; } = string.Empty;
}
