using Finora.Domain.Common;

namespace Finora.Domain.Entities;

public class SavingsObjective : BaseEntity
{
    public Guid HouseholdId { get; set; }
    public Household Household { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public decimal TargetAmount { get; set; }
    public int SortOrder { get; set; }
    public DateTime? CompletedAt { get; set; }
}
