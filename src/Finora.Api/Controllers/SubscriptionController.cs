using System.Security.Claims;
using Finora.Application.DTOs.Household;
using Finora.Application.Interfaces;
using Finora.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Finora.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SubscriptionController : ControllerBase
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly IHouseholdService _householdService;
    private readonly IAccountRepository _accountRepository;
    private readonly ITransactionRepository _transactionRepository;
    private readonly IRecurringTransactionRepository _recurringTransactionRepository;

    public SubscriptionController(
        ISubscriptionService subscriptionService,
        IHouseholdService householdService,
        IAccountRepository accountRepository,
        ITransactionRepository transactionRepository,
        IRecurringTransactionRepository recurringTransactionRepository)
    {
        _subscriptionService = subscriptionService;
        _householdService = householdService;
        _accountRepository = accountRepository;
        _transactionRepository = transactionRepository;
        _recurringTransactionRepository = recurringTransactionRepository;
    }

    private Guid? UserId
    {
        get
        {
            var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(id, out var guid) ? guid : null;
        }
    }

    private async Task<Guid?> ResolveHouseholdIdAsync(CancellationToken cancellationToken)
    {
        if (UserId is not { } userId)
            return null;

        var household = await _householdService.GetOrCreateForUserAsync(userId, cancellationToken);
        return household?.Id;
    }

    public record UpgradeSubscriptionRequest
    {
        public string Plan { get; init; } = string.Empty;
    }

    public record SubscriptionLimitsDto
    {
        public int? AccountsRemaining { get; init; }
        public int? IncomeRemainingThisMonth { get; init; }
        public int? ExpensesRemainingThisMonth { get; init; }
        public bool ObjectivesEnabled { get; init; }
        public bool MonthlyReportsEnabled { get; init; }
        public bool CanInvite { get; init; }
        public bool NeedsPrimaryAccountSelection { get; init; }
        public Guid? PrimaryAccountId { get; init; }
    }

    public record SubscriptionMeDto
    {
        public string Plan { get; init; } = string.Empty;
        public SubscriptionLimitsDto Limits { get; init; } = new();
    }

    [HttpGet("me")]
    [ProducesResponseType(typeof(SubscriptionMeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SubscriptionMeDto>> GetMySubscription(CancellationToken cancellationToken)
    {
        var householdId = await ResolveHouseholdIdAsync(cancellationToken);
        if (householdId == null)
            return NotFound();

        var plan = await _subscriptionService.GetActivePlanAsync(householdId.Value, cancellationToken);
        var effectivePlan = plan ?? SubscriptionPlan.Free;
        var (freeMulti, needsPrimary, primaryAccountId) =
            await _subscriptionService.GetFreeMultiAccountStateAsync(householdId.Value, cancellationToken);

        var now = DateTime.UtcNow;
        var from = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = from.AddMonths(1).AddTicks(-1);

        int? accountsRemaining = null;
        int? incomeRemaining = null;
        int? expensesRemaining = null;
        var objectivesEnabled = effectivePlan != SubscriptionPlan.Free;
        var monthlyReportsEnabled = objectivesEnabled;
        var canInvite = effectivePlan == SubscriptionPlan.Couple;

        if (effectivePlan == SubscriptionPlan.Free)
        {
            var accounts = await _accountRepository.GetByHouseholdIdAsync(householdId.Value, cancellationToken);
            var transactions = await _transactionRepository.GetByHouseholdAsync(householdId.Value, null, from, to, cancellationToken);
            var recurring = await _recurringTransactionRepository.GetActiveForMonthAsync(householdId.Value, now.Year, now.Month, cancellationToken);

            var incomeCount = transactions.Count(t => t.Type == TransactionType.Income)
                + recurring.Count(t => t.Type == TransactionType.Income);
            var expenseCount = transactions.Count(t => t.Type == TransactionType.Expense)
                + recurring.Count(t => t.Type == TransactionType.Expense);

            accountsRemaining = Math.Max(0, 1 - accounts.Count);
            incomeRemaining = Math.Max(0, 1 - incomeCount);
            expensesRemaining = Math.Max(0, 5 - expenseCount);
        }

        return Ok(new SubscriptionMeDto
        {
            Plan = effectivePlan.ToString(),
            Limits = new SubscriptionLimitsDto
            {
                AccountsRemaining = accountsRemaining,
                IncomeRemainingThisMonth = incomeRemaining,
                ExpensesRemainingThisMonth = expensesRemaining,
                ObjectivesEnabled = objectivesEnabled,
                MonthlyReportsEnabled = monthlyReportsEnabled,
                CanInvite = canInvite,
                NeedsPrimaryAccountSelection = freeMulti && needsPrimary,
                PrimaryAccountId = freeMulti && !needsPrimary ? primaryAccountId : null
            }
        });
    }

    [HttpPut("upgrade")]
    [ProducesResponseType(typeof(SubscriptionMeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SubscriptionMeDto>> Upgrade([FromBody] UpgradeSubscriptionRequest request, CancellationToken cancellationToken)
    {
        var householdId = await ResolveHouseholdIdAsync(cancellationToken);
        if (householdId == null)
            return NotFound();

        var value = request.Plan.Trim().ToLowerInvariant();
        var plan = value switch
        {
            "free" => SubscriptionPlan.Free,
            "pro" => SubscriptionPlan.Pro,
            "couple" => SubscriptionPlan.Couple,
            _ => (SubscriptionPlan?)null
        };

        if (plan == null)
            return BadRequest(new { code = "INVALID_PLAN", message = "Plano inválido." });

        await _subscriptionService.UpgradeAsync(householdId.Value, plan.Value, cancellationToken);

        // Re-fetch updated data
        return await GetMySubscription(cancellationToken);
    }
}

