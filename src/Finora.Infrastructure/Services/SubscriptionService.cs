using Finora.Application.Interfaces;
using Finora.Application.Options;
using Finora.Domain.Entities;
using Finora.Domain.Enums;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Finora.Infrastructure.Services;

public class SubscriptionService : ISubscriptionService
{
    private readonly ISubscriptionRepository _subscriptionRepository;
    private readonly IHouseholdRepository _householdRepository;
    private readonly IAccountRepository _accountRepository;
    private readonly ITransactionRepository _transactionRepository;
    private readonly IRecurringTransactionRepository _recurringTransactionRepository;
    private readonly INotificationRepository _notificationRepository;
    private readonly IEmailService _emailService;
    private readonly AppOptions _appOptions;
    private readonly ILogger<SubscriptionService> _logger;
    private readonly ApplicationDbContext _db;

    public SubscriptionService(
        ISubscriptionRepository subscriptionRepository,
        IHouseholdRepository householdRepository,
        IAccountRepository accountRepository,
        ITransactionRepository transactionRepository,
        IRecurringTransactionRepository recurringTransactionRepository,
        INotificationRepository notificationRepository,
        IEmailService emailService,
        IOptions<AppOptions> appOptions,
        ILogger<SubscriptionService> logger,
        ApplicationDbContext db)
    {
        _subscriptionRepository = subscriptionRepository;
        _householdRepository = householdRepository;
        _accountRepository = accountRepository;
        _transactionRepository = transactionRepository;
        _recurringTransactionRepository = recurringTransactionRepository;
        _notificationRepository = notificationRepository;
        _emailService = emailService;
        _appOptions = appOptions.Value;
        _logger = logger;
        _db = db;
    }

    public async Task<SubscriptionPlan?> GetActivePlanAsync(Guid householdId, CancellationToken cancellationToken = default)
    {
        var active = await _subscriptionRepository.GetActiveByHouseholdIdAsync(householdId, cancellationToken);
        if (active != null) return active.Plan;

        // Backward compatibility: older households may have no subscription row yet.
        var household = await _householdRepository.GetByIdAsync(householdId, cancellationToken);
        if (household?.Type == HouseholdType.Couple)
            return SubscriptionPlan.Couple;

        return SubscriptionPlan.Free;
    }

    public async Task<bool> CanAddAccountAsync(Guid householdId, CancellationToken cancellationToken = default)
    {
        var plan = await GetActivePlanAsync(householdId, cancellationToken);
        if (plan != SubscriptionPlan.Free) return true;

        var accounts = await _accountRepository.GetByHouseholdIdAsync(householdId, cancellationToken);
        return accounts.Count < 1;
    }

    public async Task<bool> CanAddTransactionAsync(
        Guid householdId,
        TransactionType type,
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        var plan = await GetActivePlanAsync(householdId, cancellationToken);
        if (plan != SubscriptionPlan.Free) return true;

        var (_, needsPrimary, _) = await GetFreeMultiAccountStateAsync(householdId, cancellationToken);
        if (needsPrimary)
            return false;

        var from = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = from.AddMonths(1).AddTicks(-1);

        var transactions = await _transactionRepository.GetByHouseholdAsync(householdId, null, from, to, cancellationToken: cancellationToken);
        var recurring = await _recurringTransactionRepository.GetActiveForMonthAsync(householdId, year, month, cancellationToken);
        var count = transactions.Count(t => t.Type == type) + recurring.Count(t => t.Type == type);

        return type switch
        {
            TransactionType.Income => count < 1,
            TransactionType.Expense => count < 5,
            _ => true
        };
    }

    public async Task<bool> CanAccessObjectivesAsync(Guid householdId, CancellationToken cancellationToken = default)
    {
        var plan = await GetActivePlanAsync(householdId, cancellationToken);
        return plan != SubscriptionPlan.Free;
    }

    public Task<bool> CanAccessMonthlyReportsAsync(Guid householdId, CancellationToken cancellationToken = default)
        => CanAccessObjectivesAsync(householdId, cancellationToken);

    public async Task<bool> CanAccessRecurringAsync(Guid householdId, CancellationToken cancellationToken = default)
    {
        var plan = await GetActivePlanAsync(householdId, cancellationToken);
        return plan != SubscriptionPlan.Free;
    }

    public async Task<bool> CanAccessAssetsAsync(Guid householdId, CancellationToken cancellationToken = default)
    {
        var plan = await GetActivePlanAsync(householdId, cancellationToken);
        return plan != SubscriptionPlan.Free;
    }

    public async Task<bool> CanAccessInvestmentsAsync(Guid householdId, CancellationToken cancellationToken = default)
    {
        var plan = await GetActivePlanAsync(householdId, cancellationToken);
        return plan != SubscriptionPlan.Free;
    }

    public async Task<DateTime?> GetPaidPlanStartDateAsync(Guid householdId, CancellationToken cancellationToken = default)
    {
        var active = await _subscriptionRepository.GetActiveByHouseholdIdAsync(householdId, cancellationToken);
        if (active == null || active.Plan == SubscriptionPlan.Free)
            return null;
        return active.StartedAt;
    }

    public async Task UpgradeAsync(Guid householdId, SubscriptionPlan plan, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        // Cancel previous active subscription
        var previous = await _subscriptionRepository.GetActiveByHouseholdIdAsync(householdId, cancellationToken);
        if (previous != null)
        {
            await _db.Subscriptions
                .Where(s => s.Id == previous.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, SubscriptionStatus.Cancelled)
                    .SetProperty(x => x.UpdatedAt, now), cancellationToken);

            // Notify if downgrading from Pro to Free (Couple handled in LeaveCoupleHouseholdAsync)
            if (previous.Plan == SubscriptionPlan.Pro && plan == SubscriptionPlan.Free)
            {
                var dedupKey = $"sub-expired:{householdId}:{previous.Id}";

                if (!await _notificationRepository.ExistsByDeduplicationKeyAsync(dedupKey, cancellationToken))
                {
                    await _notificationRepository.AddAsync(new Notification
                    {
                        Id = Guid.NewGuid(),
                        HouseholdId = householdId,
                        Type = NotificationType.SubscriptionExpired,
                        Message = "O seu plano Pro foi cancelado. Renove para manter acesso completo.",
                        RedirectUrl = "/subscription",
                        DeduplicationKey = dedupKey,
                        CreatedAt = now
                    }, cancellationToken);
                }
            }
        }

        // Don't create a subscription row for Free — absence of active = Free
        if (plan == SubscriptionPlan.Free)
            return;

        await _subscriptionRepository.CreateAsync(new Subscription
        {
            Id = Guid.NewGuid(),
            HouseholdId = householdId,
            Plan = plan,
            Status = SubscriptionStatus.Active,
            StartedAt = now,
            ExpiresAt = null,
            CreatedAt = now
        }, cancellationToken);
    }

    public async Task SyncStripeSubscriptionAsync(
        Guid householdId,
        SubscriptionPlan plan,
        SubscriptionStatus status,
        string? stripeSubscriptionId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var active = await _subscriptionRepository.GetActiveByHouseholdIdAsync(householdId, cancellationToken);

        // Cancellation / downgrade to Free: close the active row (absence of active = Free).
        if (plan == SubscriptionPlan.Free || status != SubscriptionStatus.Active)
        {
            if (active != null)
            {
                await _db.Subscriptions
                    .Where(s => s.Id == active.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.Status, SubscriptionStatus.Cancelled)
                        .SetProperty(x => x.UpdatedAt, now), cancellationToken);

                if (active.Plan != SubscriptionPlan.Free)
                    await NotifyPaidPlanCancelledAsync(householdId, active, now, cancellationToken);
            }
            return;
        }

        // Same plan already active: just refresh the Stripe id/status (idempotent re-sync).
        if (active != null && active.Plan == plan)
        {
            await _db.Subscriptions
                .Where(s => s.Id == active.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, SubscriptionStatus.Active)
                    .SetProperty(x => x.StripeSubscriptionId, stripeSubscriptionId)
                    .SetProperty(x => x.UpdatedAt, now), cancellationToken);
            return;
        }

        // New (or different) paid plan: cancel any previous active row and create a fresh one.
        if (active != null)
        {
            await _db.Subscriptions
                .Where(s => s.Id == active.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, SubscriptionStatus.Cancelled)
                    .SetProperty(x => x.UpdatedAt, now), cancellationToken);
        }

        await _subscriptionRepository.CreateAsync(new Subscription
        {
            Id = Guid.NewGuid(),
            HouseholdId = householdId,
            Plan = plan,
            Status = SubscriptionStatus.Active,
            StartedAt = now,
            ExpiresAt = null,
            StripeSubscriptionId = stripeSubscriptionId,
            CreatedAt = now
        }, cancellationToken);

        // Confirmation email — only fires here, when the plan actually becomes a new paid plan
        // (idempotent re-syncs of the same plan return earlier without reaching this branch).
        await SendSubscriptionConfirmationAsync(householdId, plan, cancellationToken);
    }

    private async Task SendSubscriptionConfirmationAsync(Guid householdId, SubscriptionPlan plan, CancellationToken cancellationToken)
    {
        try
        {
            var email = await _db.Users
                .AsNoTracking()
                .Where(u => u.HouseholdId == householdId)
                .OrderBy(u => u.CreatedAt)
                .Select(u => u.Email)
                .FirstOrDefaultAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(email))
                return;

            var manageUrl = $"{_appOptions.PublicBaseUrl.TrimEnd('/')}/subscription";
            await _emailService.SendSubscriptionConfirmationAsync(email, plan.ToString(), manageUrl, cancellationToken);
        }
        catch (Exception ex)
        {
            // Best-effort: a mail failure must never break webhook/sync processing.
            _logger.LogWarning(ex, "Falha ao enviar email de confirmação de subscrição para o agregado {HouseholdId}", householdId);
        }
    }

    private async Task NotifyPaidPlanCancelledAsync(Guid householdId, Subscription previous, DateTime now, CancellationToken cancellationToken)
    {
        var dedupKey = $"sub-expired:{householdId}:{previous.Id}";
        if (await _notificationRepository.ExistsByDeduplicationKeyAsync(dedupKey, cancellationToken))
            return;

        await _notificationRepository.AddAsync(new Notification
        {
            Id = Guid.NewGuid(),
            HouseholdId = householdId,
            Type = NotificationType.SubscriptionExpired,
            Message = "A sua subscrição foi cancelada. Renove para manter o acesso completo.",
            RedirectUrl = "/subscription",
            DeduplicationKey = dedupKey,
            CreatedAt = now
        }, cancellationToken);
    }

    public async Task<(bool FreeMultiAccount, bool NeedsPrimarySelection, Guid? PrimaryAccountId)> GetFreeMultiAccountStateAsync(
        Guid householdId,
        CancellationToken cancellationToken = default)
    {
        var plan = await GetActivePlanAsync(householdId, cancellationToken);
        if (plan != SubscriptionPlan.Free)
            return (false, false, null);

        var accounts = await _accountRepository.GetByHouseholdIdAsync(householdId, cancellationToken);
        if (accounts.Count <= 1)
            return (false, false, accounts.Count == 1 ? accounts[0].Id : null);

        var household = await _householdRepository.GetByIdAsync(householdId, cancellationToken);
        if (household == null)
            return (true, true, null);

        var pid = household.PrimaryAccountId;
        if (!pid.HasValue)
            return (true, true, null);

        if (accounts.All(a => a.Id != pid.Value))
            return (true, true, null);

        return (true, false, pid);
    }

    public async Task<bool> CanUseAccountForActivityAsync(Guid householdId, Guid accountId, CancellationToken cancellationToken = default)
    {
        var (freeMulti, needsPrimary, primaryId) = await GetFreeMultiAccountStateAsync(householdId, cancellationToken);
        if (!freeMulti)
            return true;
        if (needsPrimary)
            return false;
        return accountId == primaryId!.Value;
    }
}

