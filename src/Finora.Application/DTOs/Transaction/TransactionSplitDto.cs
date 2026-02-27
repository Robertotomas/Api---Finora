namespace Finora.Application.DTOs.Transaction;

public record TransactionSplitDto
{
    public Guid UserId { get; init; }
    public decimal Percentage { get; init; }
}
