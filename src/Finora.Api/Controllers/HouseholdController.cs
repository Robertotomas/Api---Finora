using System.Security.Claims;
using Finora.Application.DTOs.Auth;
using Finora.Application.DTOs.Household;
using Finora.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Finora.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class HouseholdController : ControllerBase
{
    private readonly IHouseholdService _householdService;
    private readonly IAuthService _authService;

    public HouseholdController(IHouseholdService householdService, IAuthService authService)
    {
        _householdService = householdService;
        _authService = authService;
    }

    private Guid? UserId
    {
        get
        {
            var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(id, out var guid) ? guid : null;
        }
    }

    /// <summary>
    /// Get current user's household. Creates one if the user doesn't have one.
    /// </summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(HouseholdDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HouseholdDto>> GetMyHousehold(CancellationToken cancellationToken)
    {
        if (UserId == null)
            return NotFound();

        var householdId = User.FindFirstValue("household_id");
        if (!string.IsNullOrEmpty(householdId) && Guid.TryParse(householdId, out var id))
        {
            var household = await _householdService.GetByIdAsync(id, UserId.Value, cancellationToken);
            if (household != null)
                return Ok(household);
        }

        var createdOrExisting = await _householdService.GetOrCreateForUserAsync(UserId.Value, cancellationToken);
        return createdOrExisting == null ? NotFound() : Ok(createdOrExisting);
    }

    /// <summary>
    /// Get household members (for couples).
    /// </summary>
    [HttpGet("members")]
    [ProducesResponseType(typeof(IReadOnlyList<HouseholdMemberDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<HouseholdMemberDto>>> GetMembers(CancellationToken cancellationToken)
    {
        if (UserId == null)
            return NotFound();

        var householdId = User.FindFirstValue("household_id");
        if (string.IsNullOrEmpty(householdId) || !Guid.TryParse(householdId, out var id))
        {
            var household = await _householdService.GetOrCreateForUserAsync(UserId.Value, cancellationToken);
            if (household == null) return NotFound();
            id = household.Id;
        }

        var members = await _householdService.GetMembersAsync(id, UserId.Value, cancellationToken);
        return Ok(members);
    }

    /// <summary>
    /// Update current user's household (authorization: user must belong to household).
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(HouseholdDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HouseholdDto>> Update(Guid id, [FromBody] UpdateHouseholdRequest request, CancellationToken cancellationToken)
    {
        if (UserId == null)
            return Forbid();

        var household = await _householdService.UpdateAsync(id, request, UserId.Value, cancellationToken);
        return household == null ? NotFound() : Ok(household);
    }

    /// <summary>Choose which account stays active on Free when the household has more than one account.</summary>
    [HttpPut("me/primary-account")]
    [ProducesResponseType(typeof(HouseholdDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HouseholdDto>> SetPrimaryAccount(
        [FromBody] SetPrimaryAccountRequest request,
        CancellationToken cancellationToken)
    {
        if (UserId == null)
            return NotFound();

        var household = await _householdService.SetPrimaryAccountAsync(UserId.Value, request, cancellationToken);
        return household == null ? NotFound() : Ok(household);
    }

    /// <summary>Leave Couple household: cancels plan for the shared household, both users end on Free; returns a new JWT with updated <c>household_id</c>.</summary>
    [HttpPost("me/leave-couple")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AuthResponse>> LeaveCouple(CancellationToken cancellationToken)
    {
        if (UserId == null)
            return Unauthorized();

        try
        {
            await _householdService.LeaveCoupleHouseholdAsync(UserId.Value, cancellationToken);
            var auth = await _authService.RefreshTokenAsync(UserId.Value, cancellationToken);
            return Ok(auth);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Clears the &quot;partner left&quot; banner for the remaining member without deleting data.</summary>
    [HttpPost("me/dismiss-partner-left-notice")]
    [ProducesResponseType(typeof(HouseholdDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HouseholdDto>> DismissPartnerLeftNotice(CancellationToken cancellationToken)
    {
        if (UserId == null)
            return Unauthorized();

        var dto = await _householdService.DismissPartnerLeftNoticeAsync(UserId.Value, cancellationToken);
        return dto == null ? NotFound() : Ok(dto);
    }

    /// <summary>Deletes all financial data for the current household (irreversible). Requires confirm phrase RECOMECAR.</summary>
    [HttpPost("me/reset-financial-data")]
    [ProducesResponseType(typeof(HouseholdDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HouseholdDto>> ResetFinancialData(
        [FromBody] ResetHouseholdFinancialDataRequest request,
        CancellationToken cancellationToken)
    {
        if (UserId == null)
            return Unauthorized();

        try
        {
            var dto = await _householdService.ResetFinancialDataAsync(UserId.Value, request.ConfirmPhrase, cancellationToken);
            return dto == null ? NotFound() : Ok(dto);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
