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

    public TransactionEntityType EntityType { get; init; } = TransactionEntityType.Entity;

    [MaxLength(200)]
    public string? EntityName { get; init; }

    public Guid? DestinationAccountId { get; init; }

    /// <summary>Membro responsável (só Couple, fora de transferências). Null = sem responsável.</summary>
    public Guid? ResponsibleUserId { get; init; }

    /// <summary>0 = Monthly, 1 = Annual, 2 = Quarterly, 3 = SemiAnnual. Defaults to Monthly.</summary>
    public int Frequency { get; init; }
    /// <summary>Non-monthly reference month (1-12) where the amount is charged. Null = spread across 12 months.</summary>
    [Range(1, 12)]
    public int? AnnualMonth { get; init; }
}
