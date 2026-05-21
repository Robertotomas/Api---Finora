using Finora.Application.Interfaces;
using Finora.Domain.Entities;
using Finora.Domain.Enums;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Finora.Infrastructure.Services;

public class NotificationGenerationService : INotificationGenerationService
{
    private readonly ApplicationDbContext _db;
    private readonly INotificationRepository _notificationRepo;
    private readonly ISubscriptionService _subscriptionService;
    private readonly ILogger<NotificationGenerationService> _logger;

    private static readonly string[] MonthNames =
    {
        "", "janeiro", "fevereiro", "março", "abril", "maio", "junho",
        "julho", "agosto", "setembro", "outubro", "novembro", "dezembro"
    };

    public NotificationGenerationService(
        ApplicationDbContext db,
        INotificationRepository notificationRepo,
        ISubscriptionService subscriptionService,
        ILogger<NotificationGenerationService> logger)
    {
        _db = db;
        _notificationRepo = notificationRepo;
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    public async Task GeneratePendingNotificationsAsync(CancellationToken cancellationToken = default)
    {
        var householdIds = await _db.Households
            .AsNoTracking()
            .Select(h => h.Id)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;

        foreach (var hid in householdIds)
        {
            try
            {
                await CheckBudgetExceededAsync(hid, now, cancellationToken);
                await CheckMonthCloseAsync(hid, now, cancellationToken);
                await CheckMonthlyPlanReminderAsync(hid, now, cancellationToken);
                await CheckSubscriptionExpiredAsync(hid, now, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Notification generation failed for household {HouseholdId}.", hid);
            }
        }
    }

    private async Task CheckBudgetExceededAsync(Guid householdId, DateTime now, CancellationToken ct)
    {
        var year = now.Year;
        var month = now.Month;

        var budget = await _db.MonthlyBudgets
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.HouseholdId == householdId && b.Year == year && b.Month == month, ct);

        if (budget == null || budget.ExpectedExpenses <= 0)
            return;

        var dedupKey = $"budget-exceeded:{householdId}:{year}:{month}";
        if (await _notificationRepo.ExistsByDeduplicationKeyAsync(dedupKey, ct))
            return;

        var totalExpenses = await _db.Transactions
            .AsNoTracking()
            .Where(t => t.HouseholdId == householdId
                && t.Type == TransactionType.Expense
                && t.Date.Year == year
                && t.Date.Month == month)
            .SumAsync(t => t.Amount, ct);

        if (totalExpenses <= budget.ExpectedExpenses)
            return;

        await _notificationRepo.AddAsync(new Notification
        {
            Id = Guid.NewGuid(),
            HouseholdId = householdId,
            Type = NotificationType.BudgetExceeded,
            Message = $"As despesas de {MonthNames[month]} ultrapassaram o orçamento de {budget.ExpectedExpenses:N2}€.",
            RedirectUrl = "/movimentos?tab=dashboard",
            DeduplicationKey = dedupKey,
            CreatedAt = DateTime.UtcNow
        }, ct);

        _logger.LogInformation("Budget exceeded notification created for household {HouseholdId}.", householdId);
    }

    private async Task CheckMonthCloseAsync(Guid householdId, DateTime now, CancellationToken ct)
    {
        // Only on 1st+ of the month — notify about the previous month
        if (now.Day < 1) return;

        // Only for Pro/Couple — Free users don't have access to reports
        if (!await _subscriptionService.CanAccessMonthlyReportsAsync(householdId, ct))
            return;

        var prevMonth = now.Month == 1 ? 12 : now.Month - 1;
        var prevYear = now.Month == 1 ? now.Year - 1 : now.Year;

        // Don't notify about months before the paid plan started
        var planStart = await _subscriptionService.GetPaidPlanStartDateAsync(householdId, ct);
        if (planStart.HasValue && new DateTime(prevYear, prevMonth, 1) < new DateTime(planStart.Value.Year, planStart.Value.Month, 1))
            return;

        var dedupKey = $"month-close:{householdId}:{prevYear}:{prevMonth}";
        if (await _notificationRepo.ExistsByDeduplicationKeyAsync(dedupKey, ct))
            return;

        // Only notify if the household has transactions in the previous month
        var hadActivity = await _db.Transactions
            .AsNoTracking()
            .AnyAsync(t => t.HouseholdId == householdId
                && t.Date.Year == prevYear
                && t.Date.Month == prevMonth, ct);

        if (!hadActivity) return;

        await _notificationRepo.AddAsync(new Notification
        {
            Id = Guid.NewGuid(),
            HouseholdId = householdId,
            Type = NotificationType.MonthClose,
            Message = $"O resumo de {MonthNames[prevMonth]} está disponível. Consulta em Relatórios.",
            RedirectUrl = "/relatorios",
            DeduplicationKey = dedupKey,
            CreatedAt = DateTime.UtcNow
        }, ct);

        _logger.LogInformation("Month close notification created for household {HouseholdId}.", householdId);
    }

    private async Task CheckMonthlyPlanReminderAsync(Guid householdId, DateTime now, CancellationToken ct)
    {
        var year = now.Year;
        var month = now.Month;
        var day = now.Day;

        // Check if budget already exists
        var hasBudget = await _db.MonthlyBudgets
            .AsNoTracking()
            .AnyAsync(b => b.HouseholdId == householdId && b.Year == year && b.Month == month, ct);

        if (hasBudget) return;

        // Determine sequence number based on day
        int seq;
        if (day <= 2) seq = 0;
        else if (day <= 6) seq = 1;
        else seq = 2 + (day - 7) / 7; // weekly from day 7+

        var dedupKey = $"plan-reminder:{householdId}:{year}:{month}:{seq}";
        if (await _notificationRepo.ExistsByDeduplicationKeyAsync(dedupKey, ct))
            return;

        await _notificationRepo.AddAsync(new Notification
        {
            Id = Guid.NewGuid(),
            HouseholdId = householdId,
            Type = NotificationType.MonthlyPlanReminder,
            Message = $"Ainda não preencheste o plano mensal de {MonthNames[month]}. Define o teu orçamento.",
            RedirectUrl = "/plano-mensal",
            DeduplicationKey = dedupKey,
            CreatedAt = DateTime.UtcNow
        }, ct);

        _logger.LogInformation("Monthly plan reminder (seq {Seq}) created for household {HouseholdId}.", seq, householdId);
    }

    private async Task CheckSubscriptionExpiredAsync(Guid householdId, DateTime now, CancellationToken ct)
    {
        // Find subscriptions that expired but are still marked Active
        var expiredSub = await _db.Subscriptions
            .FirstOrDefaultAsync(s => s.HouseholdId == householdId
                && s.Status == Domain.Enums.SubscriptionStatus.Active
                && s.ExpiresAt != null
                && s.ExpiresAt <= now, ct);

        if (expiredSub == null)
            return;

        // Mark subscription as expired
        expiredSub.Status = Domain.Enums.SubscriptionStatus.Expired;
        expiredSub.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);

        var planName = expiredSub.Plan == Domain.Enums.SubscriptionPlan.Couple ? "Couple" : "Pro";
        var dedupKey = $"sub-expired:{householdId}:{expiredSub.Id}";

        if (await _notificationRepo.ExistsByDeduplicationKeyAsync(dedupKey, ct))
            return;

        await _notificationRepo.AddAsync(new Notification
        {
            Id = Guid.NewGuid(),
            HouseholdId = householdId,
            Type = NotificationType.SubscriptionExpired,
            Message = $"O teu plano {planName} expirou. Renova para manter acesso completo.",
            RedirectUrl = "/subscricao",
            DeduplicationKey = dedupKey,
            CreatedAt = DateTime.UtcNow
        }, ct);

        _logger.LogInformation("Subscription expired notification created for household {HouseholdId}.", householdId);
    }
}
