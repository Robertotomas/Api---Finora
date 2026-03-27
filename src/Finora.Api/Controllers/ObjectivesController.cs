using System.Security.Claims;
using Finora.Application.DTOs.Objectives;
using Finora.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Finora.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ObjectivesController : ControllerBase
{
    private readonly ISavingsObjectiveService _objectivesService;
    private readonly IHouseholdService _householdService;
    private readonly ISubscriptionService _subscriptionService;

    public ObjectivesController(
        ISavingsObjectiveService objectivesService,
        IHouseholdService householdService,
        ISubscriptionService subscriptionService)
    {
        _objectivesService = objectivesService;
        _householdService = householdService;
        _subscriptionService = subscriptionService;
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

    [HttpGet]
    [ProducesResponseType(typeof(SavingsObjectivesOverviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SavingsObjectivesOverviewDto>> GetOverview(CancellationToken cancellationToken)
    {
        if (UserId == null)
            return NotFound();

        var householdId = await ResolveHouseholdIdAsync(cancellationToken);
        if (householdId == null)
            return NotFound();

        var overview = await _objectivesService.GetOverviewAsync(householdId.Value, UserId.Value, cancellationToken);
        return Ok(overview);
    }

    [HttpPost]
    [ProducesResponseType(typeof(SavingsObjectivesOverviewDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SavingsObjectivesOverviewDto>> Create(
        [FromBody] CreateSavingsObjectiveRequest request,
        CancellationToken cancellationToken)
    {
        if (UserId == null)
            return NotFound();
        if (string.IsNullOrWhiteSpace(request.Name) || request.TargetAmount <= 0)
            return BadRequest(new { message = "Nome e valor alvo são obrigatórios." });

        var householdId = await ResolveHouseholdIdAsync(cancellationToken);
        if (householdId == null)
            return NotFound();

        if (!await _subscriptionService.CanAccessObjectivesAsync(householdId.Value, cancellationToken))
            return StatusCode(StatusCodes.Status403Forbidden, new { code = "PLAN_LIMIT", message = "No plano Free não podes adicionar objetivos. Atualiza para Pro ou Couple." });

        var overview = await _objectivesService.CreateAsync(request, householdId.Value, UserId.Value, cancellationToken);
        return overview == null ? NotFound() : CreatedAtAction(nameof(GetOverview), overview);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(SavingsObjectivesOverviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SavingsObjectivesOverviewDto>> Update(
        Guid id,
        [FromBody] UpdateSavingsObjectiveRequest request,
        CancellationToken cancellationToken)
    {
        if (UserId == null)
            return NotFound();
        if (string.IsNullOrWhiteSpace(request.Name) || request.TargetAmount <= 0)
            return BadRequest(new { message = "Nome e valor alvo são obrigatórios." });

        var householdId = await ResolveHouseholdIdAsync(cancellationToken);
        if (householdId == null)
            return NotFound();

        if (!await _subscriptionService.CanAccessObjectivesAsync(householdId.Value, cancellationToken))
            return StatusCode(StatusCodes.Status403Forbidden, new { code = "PLAN_LIMIT", message = "No plano Free não podes adicionar objetivos. Atualiza para Pro ou Couple." });

        var overview = await _objectivesService.UpdateAsync(id, request, UserId.Value, cancellationToken);
        return overview == null ? NotFound() : Ok(overview);
    }

    [HttpPost("{id:guid}/finalize")]
    [ProducesResponseType(typeof(SavingsObjectivesOverviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SavingsObjectivesOverviewDto>> Finalize(Guid id, CancellationToken cancellationToken)
    {
        if (UserId == null)
            return NotFound();

        var householdId = await ResolveHouseholdIdAsync(cancellationToken);
        if (householdId == null)
            return NotFound();

        if (!await _subscriptionService.CanAccessObjectivesAsync(householdId.Value, cancellationToken))
            return StatusCode(StatusCodes.Status403Forbidden, new { code = "PLAN_LIMIT", message = "No plano Free não podes adicionar objetivos. Atualiza para Pro ou Couple." });

        var overview = await _objectivesService.FinalizeAsync(id, UserId.Value, cancellationToken);
        if (overview == null)
            return BadRequest(new { message = "Objetivo não encontrado ou ainda não está completo." });
        return Ok(overview);
    }
}
