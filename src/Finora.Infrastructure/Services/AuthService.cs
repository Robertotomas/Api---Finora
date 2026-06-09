using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Finora.Application.DTOs.Auth;
using Finora.Application.Interfaces;
using Finora.Application.Options;
using Finora.Domain.Entities;
using Finora.Domain.Enums;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Finora.Infrastructure.Services;

public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IHouseholdRepository _householdRepository;
    private readonly ISubscriptionRepository _subscriptionRepository;
    private readonly ICoupleInvitationService _coupleInvitationService;
    private readonly IEmailService _emailService;
    private readonly ApplicationDbContext _dbContext;
    private readonly JwtOptions _jwtOptions;
    private readonly AppOptions _appOptions;

    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);
    private static readonly TimeSpan PasswordResetLifetime = TimeSpan.FromHours(1);

    public AuthService(
        IUserRepository userRepository,
        IHouseholdRepository householdRepository,
        ISubscriptionRepository subscriptionRepository,
        ICoupleInvitationService coupleInvitationService,
        IEmailService emailService,
        ApplicationDbContext dbContext,
        IOptions<JwtOptions> jwtOptions,
        IOptions<AppOptions> appOptions)
    {
        _userRepository = userRepository;
        _householdRepository = householdRepository;
        _subscriptionRepository = subscriptionRepository;
        _coupleInvitationService = coupleInvitationService;
        _emailService = emailService;
        _dbContext = dbContext;
        _jwtOptions = jwtOptions.Value;
        _appOptions = appOptions.Value;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var emailNorm = request.Email.Trim().ToLowerInvariant();

        if (!string.IsNullOrWhiteSpace(request.InviteToken))
        {
            var ctx = await _coupleInvitationService.PrepareNewUserInviteAsync(emailNorm, request.InviteToken, cancellationToken);
            if (ctx == null)
                throw new InvalidOperationException("Convite inválido ou expirado.");

            if (await _userRepository.ExistsByEmailAsync(emailNorm, cancellationToken))
                throw new InvalidOperationException("User with this email already exists.");

            var invitedUser = new User
            {
                Id = Guid.NewGuid(),
                Email = emailNorm,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, BCrypt.Net.BCrypt.GenerateSalt(10)),
                FirstName = request.FirstName.Trim(),
                LastName = request.LastName.Trim(),
                Gender = request.Gender,
                TimeZoneId = NormalizeTimeZone(request.TimeZoneId),
                HouseholdId = ctx.TargetHouseholdId,
                IsCoupleGuest = true,
                CoupleJoinDataMigrated = null,
                CreatedAt = DateTime.UtcNow
            };

            await _userRepository.CreateAsync(invitedUser, cancellationToken);
            await _coupleInvitationService.CompleteNewUserInviteAsync(ctx.InvitationId, cancellationToken);
            return await GenerateAuthResponseAsync(invitedUser, cancellationToken);
        }

        if (await _userRepository.ExistsByEmailAsync(request.Email, cancellationToken))
            throw new InvalidOperationException("User with this email already exists.");

        var household = new Household
        {
            Id = Guid.NewGuid(),
            Type = HouseholdType.Individual,
            Name = $"{request.FirstName.Trim()}'s Household",
            CreatedAt = DateTime.UtcNow
        };
        await _householdRepository.CreateAsync(household, cancellationToken);

        await _subscriptionRepository.CreateAsync(new Subscription
        {
            Id = Guid.NewGuid(),
            HouseholdId = household.Id,
            Plan = SubscriptionPlan.Free,
            Status = SubscriptionStatus.Active,
            StartedAt = DateTime.UtcNow,
            ExpiresAt = null,
            CreatedAt = DateTime.UtcNow
        }, cancellationToken);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = request.Email.Trim().ToLowerInvariant(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, BCrypt.Net.BCrypt.GenerateSalt(10)),
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Gender = request.Gender,
            TimeZoneId = NormalizeTimeZone(request.TimeZoneId),
            HouseholdId = household.Id,
            CreatedAt = DateTime.UtcNow
        };

        await _userRepository.CreateAsync(user, cancellationToken);

        return await GenerateAuthResponseAsync(user, cancellationToken);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Email.ToLower() == request.Email.ToLower(), cancellationToken);

        if (user == null)
            throw new UnauthorizedAccessException("Invalid email or password.");

        // Check lockout
        if (user.LockedUntil.HasValue && user.LockedUntil.Value > DateTime.UtcNow)
        {
            var remaining = (int)Math.Ceiling((user.LockedUntil.Value - DateTime.UtcNow).TotalMinutes);
            throw new UnauthorizedAccessException($"Conta temporariamente bloqueada. Tenta novamente em {remaining} minuto(s).");
        }

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            // Increment failed attempts
            user.FailedLoginAttempts++;
            if (user.FailedLoginAttempts >= MaxFailedAttempts)
            {
                user.LockedUntil = DateTime.UtcNow.Add(LockoutDuration);
                user.FailedLoginAttempts = 0;
                await _dbContext.SaveChangesAsync(cancellationToken);
                throw new UnauthorizedAccessException($"Conta bloqueada por {(int)LockoutDuration.TotalMinutes} minutos após demasiadas tentativas falhadas.");
            }
            await _dbContext.SaveChangesAsync(cancellationToken);
            throw new UnauthorizedAccessException("Invalid email or password.");
        }

        // Reset on success
        var needsSave = false;
        if (user.FailedLoginAttempts > 0 || user.LockedUntil.HasValue)
        {
            user.FailedLoginAttempts = 0;
            user.LockedUntil = null;
            needsSave = true;
        }

        // Update timezone from browser on each login
        var tz = NormalizeTimeZone(request.TimeZoneId);
        if (tz != null && tz != user.TimeZoneId)
        {
            user.TimeZoneId = tz;
            user.UpdatedAt = DateTime.UtcNow;
            needsSave = true;
        }

        if (needsSave)
            await _dbContext.SaveChangesAsync(cancellationToken);

        return await GenerateAuthResponseAsync(user, cancellationToken);
    }

    public async Task<AuthResponse> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var tokenHash = HashToken(refreshToken);

        var stored = await _dbContext.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash && !rt.IsRevoked, cancellationToken);

        if (stored == null)
            throw new UnauthorizedAccessException("Refresh token inválido.");

        if (stored.ExpiresAt < DateTime.UtcNow)
        {
            stored.IsRevoked = true;
            await _dbContext.SaveChangesAsync(cancellationToken);
            throw new UnauthorizedAccessException("Refresh token expirado. Faz login novamente.");
        }

        // Revoke old token (rotation)
        stored.IsRevoked = true;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return await GenerateAuthResponseAsync(stored.User!, cancellationToken);
    }

    public async Task<AuthResponse> RefreshTokenForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException("Utilizador não encontrado.");
        return await GenerateAuthResponseAsync(user, cancellationToken);
    }

    public async Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default)
    {
        var emailNorm = email.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(emailNorm))
            return;

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Email.ToLower() == emailNorm, cancellationToken);

        // No email enumeration: silently succeed when the account does not exist.
        if (user == null)
            return;

        // Invalidate any previous pending reset tokens for this user.
        await _dbContext.PasswordResetTokens
            .Where(t => t.UserId == user.Id && !t.IsRevoked && t.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.IsRevoked, true), cancellationToken);

        var rawToken = InviteTokenHelper.GenerateRawToken();
        var resetToken = new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = InviteTokenHelper.Hash(rawToken),
            ExpiresAt = DateTime.UtcNow.Add(PasswordResetLifetime),
            IsRevoked = false,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.PasswordResetTokens.Add(resetToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var baseUrl = _appOptions.PublicBaseUrl.TrimEnd('/');
        var resetUrl = $"{baseUrl}/redefinir-password?token={Uri.EscapeDataString(rawToken)}";
        await _emailService.SendPasswordResetLinkAsync(emailNorm, resetUrl, cancellationToken);
    }

    public async Task ResetPasswordAsync(string rawToken, string newPassword, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
            throw new InvalidOperationException("Pedido inválido ou expirado.");

        var hash = InviteTokenHelper.Hash(rawToken.Trim());

        var token = await _dbContext.PasswordResetTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash && !t.IsRevoked && t.UsedAt == null, cancellationToken);

        if (token == null || token.User == null || token.ExpiresAt < DateTime.UtcNow)
            throw new InvalidOperationException("Pedido inválido ou expirado.");

        var user = token.User;
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword, BCrypt.Net.BCrypt.GenerateSalt(10));
        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;
        user.UpdatedAt = DateTime.UtcNow;

        token.UsedAt = DateTime.UtcNow;
        token.IsRevoked = true;
        token.UpdatedAt = DateTime.UtcNow;

        // Revoke all active sessions: the user must re-login with the new password.
        await _dbContext.RefreshTokens
            .Where(rt => rt.UserId == user.Id && !rt.IsRevoked)
            .ExecuteUpdateAsync(s => s.SetProperty(rt => rt.IsRevoked, true), cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<UserDto?> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        return user == null ? null : MapToDto(user);
    }

    public async Task<UserDto?> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdTrackedAsync(userId, cancellationToken);
        if (user == null)
            return null;

        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.Gender = request.Gender;
        if (request.TimeZoneId != null)
            user.TimeZoneId = string.IsNullOrWhiteSpace(request.TimeZoneId) ? null : request.TimeZoneId.Trim();
        await _userRepository.UpdateAsync(user, cancellationToken);
        return MapToDto(user);
    }

    private static UserDto MapToDto(User user) => new()
    {
        Id = user.Id,
        Email = user.Email,
        FirstName = user.FirstName,
        LastName = user.LastName,
        Gender = user.Gender,
        HouseholdId = user.HouseholdId,
        TimeZoneId = user.TimeZoneId,
        IsCoupleGuest = user.IsCoupleGuest,
        CoupleJoinDataMigrated = user.CoupleJoinDataMigrated
    };

    private async Task<AuthResponse> GenerateAuthResponseAsync(User user, CancellationToken cancellationToken)
    {
        var accessToken = GenerateJwtToken(user);
        var expiresIn = _jwtOptions.ExpirationMinutes * 60;

        // Generate refresh token
        var rawRefreshToken = GenerateRawRefreshToken();
        var refreshTokenEntity = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = HashToken(rawRefreshToken),
            ExpiresAt = DateTime.UtcNow.Add(RefreshTokenLifetime),
            CreatedAt = DateTime.UtcNow,
            IsRevoked = false
        };
        _dbContext.RefreshTokens.Add(refreshTokenEntity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = rawRefreshToken,
            TokenType = "Bearer",
            ExpiresIn = expiresIn,
            User = MapToDto(user)
        };
    }

    private string GenerateJwtToken(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtOptions.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString())
        };
        if (user.HouseholdId.HasValue)
            claims.Add(new Claim("household_id", user.HouseholdId.Value.ToString()));

        var token = new JwtSecurityToken(
            issuer: _jwtOptions.Issuer,
            audience: _jwtOptions.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_jwtOptions.ExpirationMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string GenerateRawRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes);
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexStringLower(bytes);
    }

    /// <summary>Validates the IANA timezone id; returns null if invalid/empty.</summary>
    private static string? NormalizeTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId)) return null;
        var trimmed = timeZoneId.Trim();
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(trimmed);
            return trimmed;
        }
        catch
        {
            return null;
        }
    }
}
