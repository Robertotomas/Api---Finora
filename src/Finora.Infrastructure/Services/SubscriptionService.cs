using Finora.Application.Interfaces;
using Finora.Domain.Entities;
using Finora.Domain.Enums;

namespace Finora.Infrastructure.Services;

public class SubscriptionService : ISubscriptionService
{
    private readonly ISubscriptionRepository _subscriptionRepository;
    private readonly IHouseholdRepository _householdRepository;
    private readonly IAccountRepository _accountRepository;
    private readonly ITransactionRepository _transactionRepository;

    public SubscriptionService(
        ISubscriptionRepository subscriptionRepository,
        IHouseholdRepository householdRepository,
        IAccountRepository accountRepository,
        ITransactionRepository transactionRepository)
    {
        _subscriptionRepository = subscriptionRepository;
        _householdRepository = householdRepository;
        _accountRepository = accountRepository;
        _transactionRepository = transactionRepository;
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

        var from = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = from.AddMonths(1).AddTicks(-1);

        var transactions = await _transactionRepository.GetByHouseholdAsync(householdId, null, from, to, cancellationToken);
        var count = transactions.Count(t => t.Type == type);

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

    public async Task UpgradeAsync(Guid householdId, SubscriptionPlan plan, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

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
}

