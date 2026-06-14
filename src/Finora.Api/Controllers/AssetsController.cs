using System.Security.Claims;
using Finora.Application.DTOs.Asset;
using Finora.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Finora.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AssetsController : ControllerBase
{
    private readonly IAssetService _assetService;
    private readonly IHouseholdService _householdService;
    private readonly ISubscriptionService _subscriptionService;

    public AssetsController(
        IAssetService assetService,
        IHouseholdService householdService,
        ISubscriptionService subscriptionService)
    {
        _assetService = assetService;
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

    private const string AssetsLockedMessage = "Os Bens e valores estão disponíveis nos planos Pro e Couple.";

    /// <summary>Get all assets for the current user's household.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AssetDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AssetDto>>> GetAll(CancellationToken cancellationToken)
    {
        if (UserId == null)
            return NotFound();

        var householdId = await ResolveHouseholdIdAsync(cancellationToken);
        if (householdId == null)
            return NotFound();

        var assets = await _assetService.GetByHouseholdAsync(householdId.Value, UserId!.Value, cancellationToken);
        return Ok(assets);
    }

    /// <summary>Get an asset by ID (with its valuations).</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(AssetDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AssetDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        if (UserId == null)
            return NotFound();

        var asset = await _assetService.GetByIdAsync(id, UserId.Value, cancellationToken);
        return asset == null ? NotFound() : Ok(asset);
    }

    /// <summary>Create a new asset (Pro/Couple only).</summary>
    [HttpPost]
    [ProducesResponseType(typeof(AssetDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AssetDto>> Create([FromBody] CreateAssetRequest request, CancellationToken cancellationToken)
    {
        if (UserId == null)
            return NotFound();

        var householdId = await ResolveHouseholdIdAsync(cancellationToken);
        if (householdId == null)
            return NotFound();

        if (!await _subscriptionService.CanAccessAssetsAsync(householdId.Value, cancellationToken))
            return StatusCode(StatusCodes.Status403Forbidden, new { code = "ASSETS_LOCKED", message = AssetsLockedMessage });

        var asset = await _assetService.CreateAsync(request, householdId.Value, UserId.Value, cancellationToken);
        return asset == null ? NotFound() : CreatedAtAction(nameof(GetById), new { id = asset.Id }, asset);
    }

    /// <summary>Update an asset (Pro/Couple only).</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(AssetDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AssetDto>> Update(Guid id, [FromBody] UpdateAssetRequest request, CancellationToken cancellationToken)
    {
        if (UserId == null)
            return NotFound();

        var householdId = await ResolveHouseholdIdAsync(cancellationToken);
        if (householdId == null)
            return NotFound();

        if (!await _subscriptionService.CanAccessAssetsAsync(householdId.Value, cancellationToken))
            return StatusCode(StatusCodes.Status403Forbidden, new { code = "ASSETS_LOCKED", message = AssetsLockedMessage });

        var asset = await _assetService.UpdateAsync(id, request, UserId.Value, cancellationToken);
        return asset == null ? NotFound() : Ok(asset);
    }

    /// <summary>Delete an asset and its valuations.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        if (UserId == null)
            return NotFound();

        var deleted = await _assetService.DeleteAsync(id, UserId.Value, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }

    /// <summary>Add a valuation to an asset (Pro/Couple only).</summary>
    [HttpPost("{id:guid}/valuations")]
    [ProducesResponseType(typeof(AssetDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AssetDto>> AddValuation(Guid id, [FromBody] AddValuationRequest request, CancellationToken cancellationToken)
    {
        if (UserId == null)
            return NotFound();

        var householdId = await ResolveHouseholdIdAsync(cancellationToken);
        if (householdId == null)
            return NotFound();

        if (!await _subscriptionService.CanAccessAssetsAsync(householdId.Value, cancellationToken))
            return StatusCode(StatusCodes.Status403Forbidden, new { code = "ASSETS_LOCKED", message = AssetsLockedMessage });

        var asset = await _assetService.AddValuationAsync(id, request, UserId.Value, cancellationToken);
        return asset == null ? NotFound() : Ok(asset);
    }

    /// <summary>Update a valuation (Pro/Couple only).</summary>
    [HttpPut("{id:guid}/valuations/{valuationId:guid}")]
    [ProducesResponseType(typeof(AssetDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AssetDto>> UpdateValuation(Guid id, Guid valuationId, [FromBody] AddValuationRequest request, CancellationToken cancellationToken)
    {
        if (UserId == null)
            return NotFound();

        var householdId = await ResolveHouseholdIdAsync(cancellationToken);
        if (householdId == null)
            return NotFound();

        if (!await _subscriptionService.CanAccessAssetsAsync(householdId.Value, cancellationToken))
            return StatusCode(StatusCodes.Status403Forbidden, new { code = "ASSETS_LOCKED", message = AssetsLockedMessage });

        var asset = await _assetService.UpdateValuationAsync(id, valuationId, request, UserId.Value, cancellationToken);
        return asset == null ? NotFound() : Ok(asset);
    }

    /// <summary>Delete a valuation (cannot delete the only remaining one).</summary>
    [HttpDelete("{id:guid}/valuations/{valuationId:guid}")]
    [ProducesResponseType(typeof(AssetDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AssetDto>> DeleteValuation(Guid id, Guid valuationId, CancellationToken cancellationToken)
    {
        if (UserId == null)
            return NotFound();

        try
        {
            var asset = await _assetService.DeleteValuationAsync(id, valuationId, UserId.Value, cancellationToken);
            return asset == null ? NotFound() : Ok(asset);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { code = "VALUATION_DELETE_ERROR", message = ex.Message });
        }
    }
}
