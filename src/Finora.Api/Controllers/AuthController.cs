using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Finora.Api.Extensions;
using Finora.Application.DTOs.Auth;
using Finora.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Finora.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>
    /// Register a new user.
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _authService.RegisterAsync(request, cancellationToken);
            return Ok(response);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already exists") || ex.Message.Contains("Convite"))
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Login with email and password.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _authService.LoginAsync(request, cancellationToken);
            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Request a password-reset link by email. Always returns 200 (no email enumeration).
    /// </summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        await _authService.RequestPasswordResetAsync(request.Email, cancellationToken);
        return Ok(new { message = "Se existir uma conta com esse email, enviámos um link para redefinir a palavra-passe." });
    }

    /// <summary>
    /// Set a new password using a valid reset token from the email link.
    /// </summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            await _authService.ResetPasswordAsync(request.Token, request.NewPassword, cancellationToken);
            return Ok(new { message = "Palavra-passe alterada com sucesso." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Exchange a refresh token for a new access + refresh token pair.
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Refresh([FromBody] RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _authService.RefreshTokenAsync(request.RefreshToken, cancellationToken);
            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Get current user info (requires authentication).
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult GetCurrentUser()
    {
        var id = User.GetUserId();
        var email = User.FindFirst(ClaimTypes.Email)?.Value
            ?? User.FindFirst(JwtRegisteredClaimNames.Email)?.Value
            ?? User.FindFirst("email")?.Value;

        if (id == null)
            return Unauthorized();

        return Ok(new
        {
            id = id.Value.ToString(),
            email = email
        });
    }

    /// <summary>
    /// Get full profile from database (requires authentication).
    /// </summary>
    [HttpGet("profile")]
    [Authorize]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserDto>> GetProfile(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        var dto = await _authService.GetProfileAsync(userId, cancellationToken);
        return dto == null ? NotFound() : Ok(dto);
    }

    /// <summary>
    /// Update profile (name and gender). Email is not changed here.
    /// </summary>
    [HttpPut("profile")]
    [Authorize]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserDto>> UpdateProfile([FromBody] UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var dto = await _authService.UpdateProfileAsync(userId, request, cancellationToken);
        return dto == null ? NotFound() : Ok(dto);
    }

    private bool TryGetUserId(out Guid userId)
    {
        userId = default;
        var id = User.GetUserId();
        if (id == null)
            return false;
        userId = id.Value;
        return true;
    }
}
