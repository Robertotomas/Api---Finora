using Finora.Domain.Common;
using Finora.Domain.Enums;

namespace Finora.Domain.Entities;

public class RecurringTransaction : BaseEntity
{
    public Guid AccountId { get; set; }
    public Account Account { get; set; } = null!;

    public Guid HouseholdId { get; set; }
    public Household Household { get; set; } = null!;

    public TransactionType Type { get; set; }
    public TransactionCategory Category { get; set; }
    public decimal Amount { get; set; }
    public string? Description { get; set; }

    /// <summary>First month (1-12) when this recurring applies.</summary>
    public int StartMonth { get; set; }
    /// <summary>First year when this recurring applies.</summary>
    public int StartYear { get; set; }
    /// <summary>When removed: first month (exclusive) when it no longer applies. Null = continues indefinitely.</summary>
    public int? EndMonth { get; set; }
    /// <summary>When removed: first year (exclusive) when it no longer applies.</summary>
    public int? EndYear { get; set; }
}
