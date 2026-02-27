using System.ComponentModel.DataAnnotations;

namespace Finora.Application.DTOs.Auth;

public record RegisterRequest
{
    [Required]
    [EmailAddress]
    [MaxLength(256)]
    public string Email { get; init; } = string.Empty;

    [Required]
    [MinLength(8, ErrorMessage = "A password deve ter pelo menos 8 caracteres.")]
    [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).{8,}$",
        ErrorMessage = "A password deve conter maiúscula, minúscula e número.")]
    public string Password { get; init; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string FirstName { get; init; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string LastName { get; init; } = string.Empty;
}
