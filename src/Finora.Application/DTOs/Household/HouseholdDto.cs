using Finora.Domain.Enums;

namespace Finora.Application.DTOs.Household;

public record HouseholdDto
{
    public Guid Id { get; init; }
    public HouseholdType Type { get; init; }
    public string Name { get; init; } = string.Empty;
}
