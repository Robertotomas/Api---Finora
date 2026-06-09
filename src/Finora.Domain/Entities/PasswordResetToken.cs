using Finora.Domain.Common;

namespace Finora.Domain.Entities;

public class PasswordResetToken : BaseEntity
{
    public Guid UserId { get; set; }
    public User? User { get; set; }

    /// <summary>SHA-256 hex of the raw reset token.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>After this instant the token cannot be used.</summary>
    public DateTime ExpiresAt { get; set; }

    public DateTime? UsedAt { get; set; }

    public bool IsRevoked { get; set; }
}
