using System.Security.Claims;
using Finora.Application.DTOs.Household;
using Finora.Application.Interfaces;
using Finora.Application.Options;
using Finora.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Finora.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SubscriptionController : ControllerBase
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly IStripeService _stripeService;
    private readonly IHouseholdService _householdService;
    private readonly IAccountRepository _accountRepository;
    private readonly ITransactionRepository _transactionRepository;
    private readonly IRecurringTransactionRepository _recurringTransactionRepository;
    private readonly StripeOptions _stripeOptions;

    public SubscriptionController(
        ISubscriptionService subscriptionService,
        IStripeService stripeService,
        IHouseholdService householdService,
        IAccountRepository accountRepository,
        ITransactionRepository transactionRepository,
        IRecurringTransactionRepository recurringTransactionRepository,
        IOptions<StripeOptions> stripeOptions)
    {
        _subscriptionService = subscriptionService;
        _stripeService = stripeService;
        _householdService = householdService;
        _accountRepository = accountRepository;
        _transactionRepository = transactionRepository;
        _recurringTransactionRepository = recurringTransactionRepository;
        _stripeOptions = stripeOptions.Value;
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

    public record CheckoutRequest
    {
        public string Plan { get; init; } = string.Empty;
        public string Interval { get; init; } = "monthly";
    }

    public record CheckoutUrlDto
    {
        public string Url { get; init; } = string.Empty;
    }

    public record PlanPriceDto
    {
        public long Monthly { get; init; }
        public long Annual { get; init; }
    }

    public record PlansDto
    {
        public string Currency { get; init; } = "eur";
        public PlanPriceDto Pro { get; init; } = new();
        public PlanPriceDto Couple { get; init; } = new();
    }

    public record SubscriptionLimitsDto
    {
        public int? AccountsRemaining { get; init; }
        public int? IncomeRemainingThisMonth { get; init; }
        public int? ExpensesRemainingThisMonth { get; init; }
        public bool ObjectivesEnabled { get; init; }
        public bool MonthlyReportsEnabled { get; init; }
        public bool RecurringEnabled { get; init; }
        public bool AssetsEnabled { get; init; }
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
            var transactions = await _transactionRepository.GetByHouseholdAsync(householdId.Value, null, from, to, cancellationToken: cancellationToken);
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
                RecurringEnabled = objectivesEnabled,
                AssetsEnabled = objectivesEnabled,
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

    [HttpGet("plans")]
    [ProducesResponseType(typeof(PlansDto), StatusCodes.Status200OK)]
    public ActionResult<PlansDto> GetPlans()
    {
        // Prices come straight from server config so the UI always shows what Stripe will actually charge.
        return Ok(new PlansDto
        {
            Currency = _stripeOptions.Currency,
            Pro = new PlanPriceDto { Monthly = _stripeOptions.ProMonthlyAmount, Annual = _stripeOptions.ProAnnualAmount },
            Couple = new PlanPriceDto { Monthly = _stripeOptions.CoupleMonthlyAmount, Annual = _stripeOptions.CoupleAnnualAmount }
        });
    }

    [HttpPost("checkout")]
    [ProducesResponseType(typeof(CheckoutUrlDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CheckoutUrlDto>> CreateCheckout([FromBody] CheckoutRequest request, CancellationToken cancellationToken)
    {
        var householdId = await ResolveHouseholdIdAsync(cancellationToken);
        if (householdId == null)
            return NotFound();

        // Only paid plans are sold via Stripe. The client never sends an amount — just the plan + interval.
        var plan = request.Plan.Trim().ToLowerInvariant() switch
        {
            "pro" => SubscriptionPlan.Pro,
            "couple" => SubscriptionPlan.Couple,
            _ => (SubscriptionPlan?)null
        };
        if (plan == null)
            return BadRequest(new { code = "INVALID_PLAN", message = "Plano inválido." });

        var interval = request.Interval.Trim().ToLowerInvariant() switch
        {
            "annual" or "yearly" or "year" => BillingInterval.Annual,
            "monthly" or "month" or "" => BillingInterval.Monthly,
            _ => (BillingInterval?)null
        };
        if (interval == null)
            return BadRequest(new { code = "INVALID_INTERVAL", message = "Periodicidade inválida." });

        try
        {
            var url = await _stripeService.CreateCheckoutUrlAsync(householdId.Value, plan.Value, interval.Value, cancellationToken);
            return Ok(new CheckoutUrlDto { Url = url });
        }
        catch (InvalidOperationException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { code = "BILLING_UNAVAILABLE", message = "Pagamentos indisponíveis de momento." });
        }
        catch (Stripe.StripeException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { code = "STRIPE_ERROR", message = "Não foi possível iniciar o pagamento. Tente novamente." });
        }
    }

    [HttpPost("portal")]
    [ProducesResponseType(typeof(CheckoutUrlDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CheckoutUrlDto>> CreatePortal(CancellationToken cancellationToken)
    {
        var householdId = await ResolveHouseholdIdAsync(cancellationToken);
        if (householdId == null)
            return NotFound();

        try
        {
            var url = await _stripeService.CreatePortalUrlAsync(householdId.Value, cancellationToken);
            return Ok(new CheckoutUrlDto { Url = url });
        }
        catch (InvalidOperationException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { code = "BILLING_UNAVAILABLE", message = "Pagamentos indisponíveis de momento." });
        }
        catch (Stripe.StripeException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { code = "STRIPE_ERROR", message = "Não foi possível abrir o portal de subscrição." });
        }
    }

    [HttpPost("sync")]
    [ProducesResponseType(typeof(SubscriptionMeDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SubscriptionMeDto>> SyncFromStripe(CancellationToken cancellationToken)
    {
        var householdId = await ResolveHouseholdIdAsync(cancellationToken);
        if (householdId == null)
            return NotFound();

        try
        {
            await _stripeService.SyncFromStripeAsync(householdId.Value, cancellationToken);
        }
        catch (Stripe.StripeException)
        {
            // Reconciliation is best-effort; the webhook is the durable path. Return current local state.
        }
        catch (InvalidOperationException)
        {
            // Stripe not configured — nothing to reconcile.
        }

        return await GetMySubscription(cancellationToken);
    }
}

