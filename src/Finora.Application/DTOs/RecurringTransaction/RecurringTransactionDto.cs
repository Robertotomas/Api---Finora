using Finora.Domain.Enums;

namespace Finora.Application.DTOs.RecurringTransaction;

public record RecurringTransactionDto
{
    public Guid Id { get; init; }
    public Guid AccountId { get; init; }
    public Guid HouseholdId { get; init; }
    public TransactionType Type { get; init; }
    public TransactionCategory Category { get; init; }
    public decimal Amount { get; init; }
    public string? Description { get; init; }
    public int StartMonth { get; init; }
    public int StartYear { get; init; }
    public int? EndMonth { get; init; }
    public int? EndYear { get; init; }
}
