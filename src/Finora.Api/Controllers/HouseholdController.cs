using System.Security.Claims;
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

    public HouseholdController(IHouseholdService householdService)
    {
        _householdService = householdService;
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
}
