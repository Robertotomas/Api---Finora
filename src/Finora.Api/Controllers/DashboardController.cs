using System.Security.Claims;
using Finora.Application.DTOs.Dashboard;
using Finora.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Finora.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly IDashboardService _dashboardService;
    private readonly IHouseholdService _householdService;

    public DashboardController(IDashboardService dashboardService, IHouseholdService householdService)
    {
        _dashboardService = dashboardService;
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

    private Guid? HouseholdIdFromClaim
    {
        get
        {
            var id = User.FindFirstValue("household_id");
            return !string.IsNullOrEmpty(id) && Guid.TryParse(id, out var guid) ? guid : null;
        }
    }

    private async Task<Guid?> ResolveHouseholdIdAsync(CancellationToken cancellationToken)
    {
        if (HouseholdIdFromClaim is { } id)
            return id;
        if (UserId is not { } userId)
            return null;
        var household = await _householdService.GetOrCreateForUserAsync(userId, cancellationToken);
        return household?.Id;
    }

    /// <summary>
    /// Get dashboard data: total balance, monthly income/expenses, expenses by category, and monthly trend for charts.
    /// </summary>
    /// <param name="year">Optional year (default: current).</param>
    /// <param name="month">Optional month for income/expenses/categories (default: current).</param>
    /// <param name="trendMonths">Number of months for trend chart (default: 6).</param>
    [HttpGet]
    [ProducesResponseType(typeof(DashboardDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Get(
        [FromQuery] int? year,
        [FromQuery] int? month,
        [FromQuery] int? trendMonths,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (UserId == null)
                return NotFound();

            var householdId = await ResolveHouseholdIdAsync(cancellationToken);
            if (householdId == null)
                return NotFound();

            var months = trendMonths.HasValue ? Math.Clamp(trendMonths.Value, 1, 24) : 6;

            var dashboard = await _dashboardService.GetDashboardAsync(
                householdId.Value,
                UserId!.Value,
                year,
                month,
                months,
                cancellationToken);

            return Ok(dashboard);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message, type = ex.GetType().Name });
        }
    }
}
