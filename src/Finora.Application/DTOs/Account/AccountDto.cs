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
}
