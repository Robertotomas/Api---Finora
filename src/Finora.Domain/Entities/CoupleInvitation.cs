using Finora.Domain.Common;
using Finora.Domain.Enums;

namespace Finora.Domain.Entities;

public class CoupleInvitation : BaseEntity
{
    public Guid InviterUserId { get; set; }
    public User? InviterUser { get; set; }

    public Guid InviterHouseholdId { get; set; }
    public Household? InviterHousehold { get; set; }

    /// <summary>Normalized lowercase email.</summary>
    public string InviteeEmail { get; set; } = string.Empty;

    public CoupleInviteKind Kind { get; set; }
    public CoupleInvitationStatus Status { get; set; }

    /// <summary>SHA-256 hex of the raw invite token (new-account flow only).</summary>
    public string? TokenHash { get; set; }

    /// <summary>SHA-256 hex of the OTP (existing-account flow only).</summary>
    public string? OtpHash { get; set; }

    public DateTime? OtpExpiresAt { get; set; }

    /// <summary>After this instant the invitation cannot be used.</summary>
    public DateTime ExpiresAt { get; set; }

    public DateTime? AcceptedAt { get; set; }
}
