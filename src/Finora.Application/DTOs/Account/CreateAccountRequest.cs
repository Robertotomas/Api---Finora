using System.ComponentModel.DataAnnotations;
using Finora.Domain.Enums;

namespace Finora.Application.DTOs.Account;

public record CreateAccountRequest
{
    [Required]
    [MaxLength(200)]
    [MinLength(1, ErrorMessage = "O nome deve ter pelo menos 1 caractere.")]
    public string Name { get; init; } = string.Empty;

    [Required]
    public AccountType Type { get; init; }

    public decimal Balance { get; init; }

    [MaxLength(3)]
    [MinLength(3)]
    public string Currency { get; init; } = "EUR";
}
