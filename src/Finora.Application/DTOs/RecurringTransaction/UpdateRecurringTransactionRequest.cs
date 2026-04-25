using System.ComponentModel.DataAnnotations;
using Finora.Domain.Enums;

namespace Finora.Application.DTOs.RecurringTransaction;

public record UpdateRecurringTransactionRequest
{
    [Required]
    public Guid AccountId { get; init; }

    [Required]
    public TransactionType Type { get; init; }

    [Required]
    public TransactionCategory Category { get; init; }

    public decimal Amount { get; init; }

    [MaxLength(500)]
    public string? Description { get; init; }

    public Guid? DestinationAccountId { get; init; }
}
