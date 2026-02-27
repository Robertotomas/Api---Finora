using Finora.Domain.Enums;

namespace Finora.Application.DTOs.Transaction;

public record TransactionDto
{
    public Guid Id { get; init; }
    public Guid AccountId { get; init; }
    public Guid HouseholdId { get; init; }
    public TransactionType Type { get; init; }
    public TransactionCategory Category { get; init; }
    public decimal Amount { get; init; }
    public DateTime Date { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<TransactionSplitDto> Splits { get; init; } = Array.Empty<TransactionSplitDto>();
}
