using Finora.Domain.Common;

namespace Finora.Domain.Entities;

public class User : BaseEntity
{
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;

    public Guid? HouseholdId { get; set; }
    public Household? Household { get; set; }
}
