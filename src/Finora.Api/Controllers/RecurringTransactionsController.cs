using System.Security.Claims;
using Finora.Application.DTOs.RecurringTransaction;
using Finora.Application.Interfaces;
using Finora.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Finora.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class RecurringTransactionsController : ControllerBase
{
    private readonly IRecurringTransactionService _recurringService;
    private readonly IHouseholdService _householdService;
    private readonly ISubscriptionService _subscriptionService;

    public RecurringTransactionsController(
        IRecurringTransactionService recurringService,
        IHouseholdService householdService,
        ISubscriptionService subscriptionService)
    {
        _recurringService = recurringService;
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

    /// <summary>Get all recurring transactions for the household.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<RecurringTransactionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<RecurringTransactionDto>>> GetAll(CancellationToken cancellationToken)
    {
        if (UserId == null)
            return NotFound();

        var householdId = await ResolveHouseholdIdAsync(cancellationToken);
        if (householdId == null)
            return NotFound();

        var list = await _recurringService.GetByHouseholdAsync(householdId.Value, UserId!.Value, cancellationToken);
        return Ok(list);
    }

    /// <summary>Get recurring transaction by ID.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(RecurringTransactionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RecurringTransactionDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        if (UserId == null)
            return NotFound();

        var item = await _recurringService.GetByIdAsync(id, UserId.Value, cancellationToken);
        return item == null ? NotFound() : Ok(item);
    }

    /// <summary>Create a new recurring transaction (starts from current month).</summary>
    [HttpPost]
    [ProducesResponseType(typeof(RecurringTransactionDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RecurringTransactionDto>> Create([FromBody] CreateRecurringTransactionRequest request, CancellationToken cancellationToken)
    {
        if (UserId == null)
            return NotFound();

        var householdId = await ResolveHouseholdIdAsync(cancellationToken);
        if (householdId == null)
            return NotFound();

        var now = DateTime.UtcNow;
        if (!await _subscriptionService.CanAddTransactionAsync(
                householdId.Value,
                request.Type,
                now.Year,
                now.Month,
                cancellationToken))
        {
            var message = request.Type == TransactionType.Income
                ? "No plano Free só podes adicionar 1 receita por mês."
                : "No plano Free só podes adicionar 5 despesas por mês.";
            return StatusCode(StatusCodes.Status403Forbidden, new { code = "PLAN_LIMIT", message });
        }

        var created = await _recurringService.CreateAsync(request, householdId.Value, UserId!.Value, cancellationToken);
        return created == null ? NotFound() : CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>Update a recurring transaction.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(RecurringTransactionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RecurringTransactionDto>> Update(Guid id, [FromBody] UpdateRecurringTransactionRequest request, CancellationToken cancellationToken)
    {
        if (UserId == null)
            return NotFound();

        var updated = await _recurringService.UpdateAsync(id, request, UserId!.Value, cancellationToken);
        return updated == null ? NotFound() : Ok(updated);
    }

    /// <summary>Remove recurring from a given month onward. It stays counted until (exclusive) that month.</summary>
    /// <param name="year">Year of the month from which to stop.</param>
    /// <param name="month">Month from which to stop (1-12).</param>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Remove(
        Guid id,
        [FromQuery] int year,
        [FromQuery] int month,
        CancellationToken cancellationToken)
    {
        if (UserId == null)
            return NotFound();

        if (month < 1 || month > 12)
            return BadRequest(new { message = "Month must be between 1 and 12." });

        var removed = await _recurringService.RemoveFromMonthAsync(id, year, month, UserId!.Value, cancellationToken);
        return removed ? NoContent() : NotFound();
    }
}
