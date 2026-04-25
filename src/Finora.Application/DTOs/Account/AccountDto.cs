using Finora.Domain.Enums;

namespace Finora.Application.DTOs.Account;

public record AccountDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public AccountType Type { get; init; }
    public decimal Balance { get; init; }
    public string Currency { get; init; } = "EUR";
    public Guid HouseholdId { get; init; }
    /// <summary>False when Free + 2+ accounts and this account is not the chosen primary (or primary not chosen yet).</summary>
    public bool IsActiveForPlan { get; init; } = true;
    public bool IsArchived { get; init; }
    public DateTime? ArchivedAt { get; init; }
}
