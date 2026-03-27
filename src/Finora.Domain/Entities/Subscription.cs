using Finora.Domain.Common;
using Finora.Domain.Enums;

namespace Finora.Domain.Entities;

public class Subscription : BaseEntity
{
    public Guid HouseholdId { get; set; }
    public Household Household { get; set; } = null!;

    public SubscriptionPlan Plan { get; set; }
    public SubscriptionStatus Status { get; set; }

    public DateTime StartedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

