namespace Finora.Application.DTOs.Household;

public record SetPrimaryAccountRequest
{
    public Guid AccountId { get; init; }
}
