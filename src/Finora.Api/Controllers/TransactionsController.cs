using System.Security.Claims;
using Finora.Application.DTOs.Transaction;
using Finora.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Finora.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TransactionsController : ControllerBase
{
    private readonly ITransactionService _transactionService;
    private readonly IHouseholdService _householdService;
    private readonly ISubscriptionService _subscriptionService;

    public TransactionsController(
        ITransactionService transactionService,
        IHouseholdService householdService,
        ISubscriptionService subscriptionService)
    {
        _transactionService = transactionService;
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

    /// <summary>
    /// Get transactions for the current user's household.
    /// </summary>
    /// <param name="accountId">Optional filter by account.</param>
    /// <param name="from">Optional start date (yyyy-MM-dd, inclusive).</param>
    /// <param name="to">Optional end date (yyyy-MM-dd, inclusive).</param>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<TransactionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TransactionDto>>> GetAll(
        [FromQuery] Guid? accountId,
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken cancellationToken)
    {
        if (UserId == null)
            return NotFound();

        var householdId = await ResolveHouseholdIdAsync(cancellationToken);
        if (householdId == null)
            return NotFound();

        DateTime? fromDate = null;
        DateTime? toDate = null;
        if (!string.IsNullOrWhiteSpace(from) && DateTime.TryParse(from, out var fd))
            fromDate = DateTime.SpecifyKind(fd.Date, DateTimeKind.Utc);
        if (!string.IsNullOrWhiteSpace(to) && DateTime.TryParse(to, out var td))
            toDate = DateTime.SpecifyKind(td.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);

        var transactions = await _transactionService.GetByHouseholdAsync(householdId.Value, UserId!.Value, accountId, fromDate, toDate, cancellationToken);
        return Ok(transactions);
    }

    /// <summary>
    /// Get transaction by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(TransactionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TransactionDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        if (UserId == null)
            return NotFound();

        var transaction = await _transactionService.GetByIdAsync(id, UserId.Value, cancellationToken);
        return transaction == null ? NotFound() : Ok(transaction);
    }

    /// <summary>
    /// Create a new transaction.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(TransactionDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TransactionDto>> Create([FromBody] CreateTransactionRequest request, CancellationToken cancellationToken)
    {
        if (UserId == null)
            return NotFound();

        var householdId = await ResolveHouseholdIdAsync(cancellationToken);
        if (householdId == null)
            return NotFound();

        var (_, needsPrimary, _) = await _subscriptionService.GetFreeMultiAccountStateAsync(householdId.Value, cancellationToken);
        if (needsPrimary)
            return StatusCode(StatusCodes.Status403Forbidden, new { code = "FREE_PRIMARY_REQUIRED", message = "Tens mais do que uma conta no plano Free. Escolhe a conta principal em Contas antes de adicionar movimentos." });

        if (!await _subscriptionService.CanUseAccountForActivityAsync(householdId.Value, request.AccountId, cancellationToken))
            return StatusCode(StatusCodes.Status403Forbidden, new { code = "FREE_ACCOUNT_LOCKED", message = "No plano Free só podes usar a conta principal. Altera a conta principal em Contas ou elimina contas extra." });

        if (!await _subscriptionService.CanAddTransactionAsync(
                householdId.Value,
                request.Type,
                request.Date.Year,
                request.Date.Month,
                cancellationToken))
            return StatusCode(StatusCodes.Status403Forbidden, new { code = "PLAN_LIMIT", message = "No plano Free só podes adicionar 1 receita e 5 despesas por mês." });

        var transaction = await _transactionService.CreateAsync(request, householdId.Value, UserId.Value, cancellationToken);
        return transaction == null ? NotFound() : CreatedAtAction(nameof(GetById), new { id = transaction.Id }, transaction);
    }

    /// <summary>
    /// Update a transaction.
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(TransactionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TransactionDto>> Update(Guid id, [FromBody] UpdateTransactionRequest request, CancellationToken cancellationToken)
    {
        if (UserId == null)
            return NotFound();

        var existing = await _transactionService.GetByIdAsync(id, UserId.Value, cancellationToken);
        if (existing == null)
            return NotFound();

        var (_, needsPrimary, _) = await _subscriptionService.GetFreeMultiAccountStateAsync(existing.HouseholdId, cancellationToken);
        if (needsPrimary)
            return StatusCode(StatusCodes.Status403Forbidden, new { code = "FREE_PRIMARY_REQUIRED", message = "Tens mais do que uma conta no plano Free. Escolhe a conta principal em Contas antes de editar movimentos." });

        if (!await _subscriptionService.CanUseAccountForActivityAsync(existing.HouseholdId, existing.AccountId, cancellationToken)
            || !await _subscriptionService.CanUseAccountForActivityAsync(existing.HouseholdId, request.AccountId, cancellationToken))
            return StatusCode(StatusCodes.Status403Forbidden, new { code = "FREE_ACCOUNT_LOCKED", message = "No plano Free só podes alterar movimentos na conta principal." });

        var transaction = await _transactionService.UpdateAsync(id, request, UserId.Value, cancellationToken);
        return transaction == null ? NotFound() : Ok(transaction);
    }

    /// <summary>
    /// Delete a transaction.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        if (UserId == null)
            return NotFound();

        var deleted = await _transactionService.DeleteAsync(id, UserId.Value, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }
}
