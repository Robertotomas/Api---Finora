namespace Finora.Domain.Entities;

/// <summary>
/// Represents a user's share of a transaction (for couples).
/// Percentage is 0-100. For individuals, typically one split at 100%.
/// </summary>
public class TransactionSplit
{
    public Guid TransactionId { get; set; }
    public Transaction Transaction { get; set; } = null!;

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public decimal Percentage { get; set; }
}
