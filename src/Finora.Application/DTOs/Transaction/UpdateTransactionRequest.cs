using System.ComponentModel.DataAnnotations;
using Finora.Domain.Enums;

namespace Finora.Application.DTOs.Transaction;

public record UpdateTransactionRequest
{
    [Required]
    public Guid AccountId { get; init; }

    [Required]
    public TransactionType Type { get; init; }

    [Required]
    public TransactionCategory Category { get; init; }

    public decimal Amount { get; init; }

    [Required]
    public DateTime Date { get; init; }

    [MaxLength(500)]
    public string? Description { get; init; }

    public IReadOnlyList<TransactionSplitInput>? Splits { get; init; }
}
