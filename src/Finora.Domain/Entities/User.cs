using Finora.Domain.Common;
using Finora.Domain.Enums;

namespace Finora.Domain.Entities;

public class User : BaseEntity
{
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public Gender? Gender { get; set; }

    public Guid? HouseholdId { get; set; }
    public Household? Household { get; set; }

    /// <summary>IANA timezone id (e.g. Europe/Lisbon) for scheduling monthly reports.</summary>
    public string? TimeZoneId { get; set; }

    /// <summary>True when this user joined the household as the invited partner (convite casal).</summary>
    public bool IsCoupleGuest { get; set; }

    /// <summary>
    /// When <see cref="IsCoupleGuest"/> is true: whether personal data was migrated from a previous household on join (OTP flow).
    /// Null when not applicable (e.g. conta criada por link de convite sem dados prévios).
    /// </summary>
    public bool? CoupleJoinDataMigrated { get; set; }
}
