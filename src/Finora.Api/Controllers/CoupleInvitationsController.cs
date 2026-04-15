using System.Security.Claims;
using Finora.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Finora.Api.Controllers;

[ApiController]
[Route("api/couple-invitations")]
public class CoupleInvitationsController : ControllerBase
{
    private readonly ICoupleInvitationService _coupleInvitationService;

    public CoupleInvitationsController(ICoupleInvitationService coupleInvitationService)
    {
        _coupleInvitationService = coupleInvitationService;
    }

    private Guid? UserId
    {
        get
        {
            var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(id, out var guid) ? guid : null;
        }
    }

    public record CreateInvitationRequest
    {
        public string Email { get; init; } = string.Empty;
    }

    public record VerifyOtpRequest
    {
        public string Code { get; init; } = string.Empty;
    }

    /// <summary>Invite partner by email (Couple plan). Sends signup link or OTP.</summary>
    [HttpPost]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateInvitationRequest request, CancellationToken cancellationToken)
    {
        if (UserId is not { } uid)
            return Unauthorized();

        try
        {
            await _coupleInvitationService.CreateInvitationAsync(uid, request.Email, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Validate invite token before registration (public).</summary>
    [HttpGet("validate")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Validate([FromQuery] string token, CancellationToken cancellationToken)
    {
        var result = await _coupleInvitationService.ValidateInviteTokenAsync(token ?? string.Empty, cancellationToken);
        if (!result.Valid)
            return Ok(new { valid = false });

        return Ok(new
        {
            valid = true,
            inviterName = result.InviterName,
            householdName = result.HouseholdName,
            inviteeEmailMasked = result.InviteeEmailMasked
        });
    }

    /// <summary>Confirm OTP for existing account (logged-in user).</summary>
    [HttpPost("verify-otp")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpRequest request, CancellationToken cancellationToken)
    {
        if (UserId is not { } uid)
            return Unauthorized();

        try
        {
            await _coupleInvitationService.VerifyOtpAndJoinAsync(uid, request.Code, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
