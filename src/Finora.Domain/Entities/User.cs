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
}
