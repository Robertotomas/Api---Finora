using System.ComponentModel.DataAnnotations;
using Finora.Domain.Enums;

namespace Finora.Application.DTOs.RecurringTransaction;

public record CreateRecurringTransactionRequest
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

    /// <summary>0 = Monthly, 1 = Annual. Defaults to Monthly.</summary>
    public int Frequency { get; init; }
    /// <summary>For Annual: the month (1-12) when payment occurs.</summary>
    public int? AnnualMonth { get; init; }
}
