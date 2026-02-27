using System.Security.Claims;
using Finora.Application.DTOs.Account;
using Finora.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Finora.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AccountsController : ControllerBase
{
    private readonly IAccountService _accountService;
    private readonly IHouseholdService _householdService;

    public AccountsController(IAccountService accountService, IHouseholdService householdService)
    {
        _accountService = accountService;
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
    /// Get all accounts for the current user's household.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AccountDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AccountDto>>> GetAll(CancellationToken cancellationToken)
    {
        if (UserId == null)
            return NotFound();

        var householdId = await ResolveHouseholdIdAsync(cancellationToken);
        if (householdId == null)
            return NotFound();

        var accounts = await _accountService.GetByHouseholdAsync(householdId.Value, UserId!.Value, cancellationToken);
        return Ok(accounts);
    }

    /// <summary>
    /// Get account by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(AccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AccountDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        if (UserId == null)
            return NotFound();

        var account = await _accountService.GetByIdAsync(id, UserId.Value, cancellationToken);
        return account == null ? NotFound() : Ok(account);
    }

    /// <summary>
    /// Create a new account.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(AccountDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AccountDto>> Create([FromBody] CreateAccountRequest request, CancellationToken cancellationToken)
    {
        if (UserId == null)
            return NotFound();

        var householdId = await ResolveHouseholdIdAsync(cancellationToken);
        if (householdId == null)
            return NotFound();

        var account = await _accountService.CreateAsync(request, householdId.Value, UserId.Value, cancellationToken);
        return account == null ? NotFound() : CreatedAtAction(nameof(GetById), new { id = account.Id }, account);
    }

    /// <summary>
    /// Update an account.
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(AccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AccountDto>> Update(Guid id, [FromBody] UpdateAccountRequest request, CancellationToken cancellationToken)
    {
        if (UserId == null)
            return NotFound();

        var account = await _accountService.UpdateAsync(id, request, UserId.Value, cancellationToken);
        return account == null ? NotFound() : Ok(account);
    }

    /// <summary>
    /// Delete an account.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        if (UserId == null)
            return NotFound();

        var deleted = await _accountService.DeleteAsync(id, UserId.Value, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }
}
