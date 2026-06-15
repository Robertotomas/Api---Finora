using Finora.Domain.Common;
using Finora.Domain.Enums;

namespace Finora.Domain.Entities;

public class Notification : BaseEntity
{
    public Guid HouseholdId { get; set; }
    public Household? Household { get; set; }
    public Guid? UserId { get; set; }
    public User? User { get; set; }
    public NotificationType Type { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? RedirectUrl { get; set; }
    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }
    public string? DeduplicationKey { get; set; }
}
