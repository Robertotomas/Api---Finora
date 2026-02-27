using System.ComponentModel.DataAnnotations;
using Finora.Domain.Enums;

namespace Finora.Application.DTOs.Transaction;

public record CreateTransactionRequest
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

    /// <summary>
    /// Splits for couples. Percentages must sum to 100. For individuals, omit or use single 100% for current user.
    /// </summary>
    public IReadOnlyList<TransactionSplitInput>? Splits { get; init; }
}

public record TransactionSplitInput
{
    public Guid UserId { get; init; }
    public decimal Percentage { get; init; }
}
