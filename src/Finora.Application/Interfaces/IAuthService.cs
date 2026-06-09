using Finora.Application.DTOs.Auth;

namespace Finora.Application.Interfaces;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task<UserDto?> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<UserDto?> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken cancellationToken = default);

    /// <summary>Exchange a valid refresh token for a new access + refresh token pair.</summary>
    Task<AuthResponse> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>New JWT after household or other server-side identity changes (internal use).</summary>
    Task<AuthResponse> RefreshTokenForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a password-reset link to the given email if an account exists.
    /// Always completes silently (no email enumeration) regardless of whether the account exists.
    /// </summary>
    Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets a new password from a valid reset token. Throws <see cref="InvalidOperationException"/>
    /// when the token is invalid, expired or already used.
    /// </summary>
    Task ResetPasswordAsync(string rawToken, string newPassword, CancellationToken cancellationToken = default);
}
