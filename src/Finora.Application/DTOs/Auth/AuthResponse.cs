using Finora.Domain.Enums;

namespace Finora.Application.DTOs.Auth;

public record AuthResponse
{
    public string AccessToken { get; init; } = string.Empty;
    public string TokenType { get; init; } = "Bearer";
    public int ExpiresIn { get; init; }
    public UserDto User { get; init; } = null!;
}

public record UserDto
{
    public Guid Id { get; init; }
    public string Email { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public Gender? Gender { get; init; }
    public Guid? HouseholdId { get; init; }
    public string? TimeZoneId { get; init; }
    public bool IsCoupleGuest { get; init; }
    public bool? CoupleJoinDataMigrated { get; init; }
}
