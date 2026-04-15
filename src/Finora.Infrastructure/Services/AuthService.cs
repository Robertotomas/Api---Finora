using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Finora.Application.DTOs.Auth;
using Finora.Application.Interfaces;
using Finora.Application.Options;
using Finora.Domain.Entities;
using Finora.Domain.Enums;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Finora.Infrastructure.Services;

public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IHouseholdRepository _householdRepository;
    private readonly ISubscriptionRepository _subscriptionRepository;
    private readonly ICoupleInvitationService _coupleInvitationService;
    private readonly JwtOptions _jwtOptions;

    public AuthService(
        IUserRepository userRepository,
        IHouseholdRepository householdRepository,
        ISubscriptionRepository subscriptionRepository,
        ICoupleInvitationService coupleInvitationService,
        IOptions<JwtOptions> jwtOptions)
    {
        _userRepository = userRepository;
        _householdRepository = householdRepository;
        _subscriptionRepository = subscriptionRepository;
        _coupleInvitationService = coupleInvitationService;
        _jwtOptions = jwtOptions.Value;
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
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, BCrypt.Net.BCrypt.GenerateSalt(12)),
                FirstName = request.FirstName.Trim(),
                LastName = request.LastName.Trim(),
                Gender = request.Gender,
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
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, BCrypt.Net.BCrypt.GenerateSalt(12)),
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Gender = request.Gender,
            HouseholdId = household.Id,
            CreatedAt = DateTime.UtcNow
        };

        await _userRepository.CreateAsync(user, cancellationToken);

        return await GenerateAuthResponseAsync(user, cancellationToken);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByEmailAsync(request.Email, cancellationToken);
        if (user == null)
            throw new UnauthorizedAccessException("Invalid email or password.");

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            throw new UnauthorizedAccessException("Invalid email or password.");

        return await GenerateAuthResponseAsync(user, cancellationToken);
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

    public async Task<AuthResponse> RefreshTokenAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException("Utilizador não encontrado.");
        return await GenerateAuthResponseAsync(user, cancellationToken);
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

    private Task<AuthResponse> GenerateAuthResponseAsync(User user, CancellationToken cancellationToken)
    {
        var token = GenerateJwtToken(user);
        var expiresIn = _jwtOptions.ExpirationMinutes * 60;

        var response = new AuthResponse
        {
            AccessToken = token,
            TokenType = "Bearer",
            ExpiresIn = expiresIn,
            User = MapToDto(user)
        };

        return Task.FromResult(response);
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
}
