using Finora.Domain.Common;
using Finora.Domain.Enums;

namespace Finora.Domain.Entities;

public class Transaction : BaseEntity
{
    public Guid AccountId { get; set; }
    public Account Account { get; set; } = null!;

    public Guid HouseholdId { get; set; }
    public Household Household { get; set; } = null!;

    public TransactionType Type { get; set; }
    public TransactionCategory Category { get; set; }
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public string? Description { get; set; }

    public ICollection<TransactionSplit> Splits { get; set; } = new List<TransactionSplit>();
}
