using Finora.Application.DTOs.Auth;

namespace Finora.Application.Interfaces;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task<UserDto?> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<UserDto?> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken cancellationToken = default);

    /// <summary>New JWT after household or other server-side identity changes.</summary>
    Task<AuthResponse> RefreshTokenAsync(Guid userId, CancellationToken cancellationToken = default);
}
