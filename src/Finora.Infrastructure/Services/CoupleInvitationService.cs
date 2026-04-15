using Finora.Application.DTOs.CoupleInvitation;
using Finora.Application.Interfaces;
using Finora.Application.Options;
using Finora.Domain.Entities;
using Finora.Domain.Enums;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Finora.Infrastructure.Services;

public class CoupleInvitationService : ICoupleInvitationService
{
    private const int InvitationValidDays = 7;
    private const int OtpValidMinutes = 15;

    private readonly ICoupleInvitationRepository _invitationRepository;
    private readonly IUserRepository _userRepository;
    private readonly IHouseholdRepository _householdRepository;
    private readonly ISubscriptionService _subscriptionService;
    private readonly IAccountRepository _accountRepository;
    private readonly ITransactionRepository _transactionRepository;
    private readonly IRecurringTransactionRepository _recurringTransactionRepository;
    private readonly ISavingsObjectiveRepository _savingsObjectiveRepository;
    private readonly IMonthlyReportRepository _monthlyReportRepository;
    private readonly IEmailService _emailService;
    private readonly AppOptions _appOptions;
    private readonly ApplicationDbContext _db;

    public CoupleInvitationService(
        ICoupleInvitationRepository invitationRepository,
        IUserRepository userRepository,
        IHouseholdRepository householdRepository,
        ISubscriptionService subscriptionService,
        IAccountRepository accountRepository,
        ITransactionRepository transactionRepository,
        IRecurringTransactionRepository recurringTransactionRepository,
        ISavingsObjectiveRepository savingsObjectiveRepository,
        IMonthlyReportRepository monthlyReportRepository,
        IEmailService emailService,
        IOptions<AppOptions> appOptions,
        ApplicationDbContext db)
    {
        _invitationRepository = invitationRepository;
        _userRepository = userRepository;
        _householdRepository = householdRepository;
        _subscriptionService = subscriptionService;
        _accountRepository = accountRepository;
        _transactionRepository = transactionRepository;
        _recurringTransactionRepository = recurringTransactionRepository;
        _savingsObjectiveRepository = savingsObjectiveRepository;
        _monthlyReportRepository = monthlyReportRepository;
        _emailService = emailService;
        _appOptions = appOptions.Value;
        _db = db;
    }

    public async Task CreateInvitationAsync(Guid inviterUserId, string inviteeEmail, CancellationToken cancellationToken = default)
    {
        var emailNorm = inviteeEmail.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(emailNorm))
            throw new InvalidOperationException("Email inválido.");

        var inviter = await _userRepository.GetByIdAsync(inviterUserId, cancellationToken)
            ?? throw new InvalidOperationException("Utilizador não encontrado.");
        if (inviter.HouseholdId == null)
            throw new InvalidOperationException("Agregado não encontrado.");

        var householdId = inviter.HouseholdId.Value;
        var members = await _userRepository.GetByHouseholdIdAsync(householdId, cancellationToken);
        if (members.Count >= 2)
            throw new InvalidOperationException("Este agregado já tem dois membros.");

        if (members.Any(m => m.Email.Equals(emailNorm, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Este email já pertence ao teu agregado.");

        // Um convite novo substitui qualquer convite pendente (incl. outro email), para permitir reenvio e testes.
        await _invitationRepository.RevokeAllPendingForHouseholdAsync(householdId, cancellationToken);

        var inviterName = $"{inviter.FirstName} {inviter.LastName}".Trim();
        if (string.IsNullOrEmpty(inviterName))
            inviterName = inviter.Email;

        var expiresAt = DateTime.UtcNow.AddDays(InvitationValidDays);

        if (await _userRepository.ExistsByEmailAsync(emailNorm, cancellationToken))
        {
            var otp = InviteTokenHelper.GenerateOtp();
            var otpHash = InviteTokenHelper.Hash(otp);
            var otpExpires = DateTime.UtcNow.AddMinutes(OtpValidMinutes);

            var invitation = new CoupleInvitation
            {
                Id = Guid.NewGuid(),
                InviterUserId = inviterUserId,
                InviterHouseholdId = householdId,
                InviteeEmail = emailNorm,
                Kind = CoupleInviteKind.ExistingAccount,
                Status = CoupleInvitationStatus.Pending,
                TokenHash = null,
                OtpHash = otpHash,
                OtpExpiresAt = otpExpires,
                ExpiresAt = expiresAt,
                CreatedAt = DateTime.UtcNow
            };

            await _emailService.SendCoupleInviteOtpAsync(emailNorm, inviterName, otp, cancellationToken);
            await EnsureCouplePlanAndPersistInvitationAsync(householdId, invitation, cancellationToken);
        }
        else
        {
            var rawToken = InviteTokenHelper.GenerateRawToken();
            var tokenHash = InviteTokenHelper.Hash(rawToken);

            var invitation = new CoupleInvitation
            {
                Id = Guid.NewGuid(),
                InviterUserId = inviterUserId,
                InviterHouseholdId = householdId,
                InviteeEmail = emailNorm,
                Kind = CoupleInviteKind.NewAccount,
                Status = CoupleInvitationStatus.Pending,
                TokenHash = tokenHash,
                OtpHash = null,
                OtpExpiresAt = null,
                ExpiresAt = expiresAt,
                CreatedAt = DateTime.UtcNow
            };

            var baseUrl = _appOptions.PublicBaseUrl.TrimEnd('/');
            var registerUrl = $"{baseUrl}/register?invite={Uri.EscapeDataString(rawToken)}";
            await _emailService.SendCoupleInviteLinkAsync(emailNorm, inviterName, registerUrl, cancellationToken);
            await EnsureCouplePlanAndPersistInvitationAsync(householdId, invitation, cancellationToken);
        }
    }

    /// <summary>
    /// Só altera o plano para Couple e grava o convite depois do email ser enviado com sucesso.
    /// </summary>
    private async Task EnsureCouplePlanAndPersistInvitationAsync(
        Guid householdId,
        CoupleInvitation invitation,
        CancellationToken cancellationToken)
    {
        var plan = await _subscriptionService.GetActivePlanAsync(householdId, cancellationToken);
        if (plan != SubscriptionPlan.Couple)
            await _subscriptionService.UpgradeAsync(householdId, SubscriptionPlan.Couple, cancellationToken);

        await _invitationRepository.AddAsync(invitation, cancellationToken);
    }

    public async Task<ValidateInviteTokenResult> ValidateInviteTokenAsync(string rawToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
            return new ValidateInviteTokenResult { Valid = false };

        var hash = InviteTokenHelper.Hash(rawToken.Trim());
        var inv = await _invitationRepository.GetPendingByTokenHashAsync(hash, cancellationToken);
        if (inv == null || inv.ExpiresAt < DateTime.UtcNow)
            return new ValidateInviteTokenResult { Valid = false };

        var plan = await _subscriptionService.GetActivePlanAsync(inv.InviterHouseholdId, cancellationToken);
        if (plan != SubscriptionPlan.Couple)
            return new ValidateInviteTokenResult { Valid = false };

        var users = await _userRepository.GetByHouseholdIdAsync(inv.InviterHouseholdId, cancellationToken);
        if (users.Count >= 2)
            return new ValidateInviteTokenResult { Valid = false };

        var inviter = inv.InviterUser;
        var inviterName = inviter != null
            ? $"{inviter.FirstName} {inviter.LastName}".Trim()
            : string.Empty;
        if (string.IsNullOrEmpty(inviterName) && inviter != null)
            inviterName = inviter.Email;

        return new ValidateInviteTokenResult
        {
            Valid = true,
            InviterName = inviterName,
            HouseholdName = inv.InviterHousehold?.Name ?? string.Empty,
            InviteeEmailMasked = InviteTokenHelper.MaskEmail(inv.InviteeEmail)
        };
    }

    public async Task<NewUserInviteContext?> PrepareNewUserInviteAsync(
        string emailNormalized,
        string rawToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
            return null;

        var hash = InviteTokenHelper.Hash(rawToken.Trim());
        var inv = await _invitationRepository.GetPendingByTokenHashAsync(hash, cancellationToken);
        if (inv == null || inv.ExpiresAt < DateTime.UtcNow)
            return null;
        if (inv.InviteeEmail != emailNormalized)
            return null;
        if (inv.Kind != CoupleInviteKind.NewAccount)
            return null;

        var plan = await _subscriptionService.GetActivePlanAsync(inv.InviterHouseholdId, cancellationToken);
        if (plan != SubscriptionPlan.Couple)
            return null;

        var users = await _userRepository.GetByHouseholdIdAsync(inv.InviterHouseholdId, cancellationToken);
        if (users.Count >= 2)
            return null;

        return new NewUserInviteContext
        {
            InvitationId = inv.Id,
            TargetHouseholdId = inv.InviterHouseholdId
        };
    }

    public async Task CompleteNewUserInviteAsync(Guid invitationId, CancellationToken cancellationToken = default)
    {
        var inv = await _invitationRepository.GetByIdTrackedAsync(invitationId, cancellationToken)
            ?? throw new InvalidOperationException("Convite não encontrado.");

        inv.Status = CoupleInvitationStatus.Accepted;
        inv.AcceptedAt = DateTime.UtcNow;
        inv.UpdatedAt = DateTime.UtcNow;

        var household = await _householdRepository.GetByIdTrackedAsync(inv.InviterHouseholdId, cancellationToken);
        if (household != null)
        {
            household.Type = HouseholdType.Couple;
            household.UpdatedAt = DateTime.UtcNow;
        }

        await _invitationRepository.SaveChangesAsync(cancellationToken);
    }

    public async Task VerifyOtpAndJoinAsync(Guid userId, string otpCode, bool migratePersonalData, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdTrackedAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException("Utilizador não encontrado.");

        var emailNorm = user.Email.Trim().ToLowerInvariant();
        var hash = InviteTokenHelper.Hash(otpCode.Trim());

        var inv = await _invitationRepository.GetPendingExistingAccountInviteByEmailAndOtpHashAsync(emailNorm, hash, cancellationToken)
            ?? throw new InvalidOperationException("Código inválido ou expirado.");

        if (inv.InviterHouseholdId == user.HouseholdId)
            throw new InvalidOperationException("Já pertences a este agregado.");

        var plan = await _subscriptionService.GetActivePlanAsync(inv.InviterHouseholdId, cancellationToken);
        if (plan != SubscriptionPlan.Couple)
            throw new InvalidOperationException("O convite já não é válido.");

        var targetMembers = await _userRepository.GetByHouseholdIdAsync(inv.InviterHouseholdId, cancellationToken);
        if (targetMembers.Count >= 2)
            throw new InvalidOperationException("Este agregado já está completo.");

        if (user.HouseholdId == null)
            throw new InvalidOperationException("Agregado não encontrado.");

        var oldHouseholdId = user.HouseholdId.Value;

        var soleInMine = await _userRepository.GetByHouseholdIdAsync(oldHouseholdId, cancellationToken);
        if (soleInMine.Count != 1)
            throw new InvalidOperationException(
                "Só podes aceitar o convite se fores o único membro do teu agregado atual.");

        if (migratePersonalData)
        {
            var hadPersonalData = await HasPersonalHouseholdDataAsync(oldHouseholdId, cancellationToken);
            await MigratePersonalHouseholdDataAsync(oldHouseholdId, inv.InviterHouseholdId, cancellationToken);
            user.CoupleJoinDataMigrated = hadPersonalData;
        }
        else
        {
            await EnsureInviteeHouseholdIsEmptyAsync(userId, cancellationToken);
            user.CoupleJoinDataMigrated = false;
        }

        user.IsCoupleGuest = true;

        var targetHousehold = await _householdRepository.GetByIdTrackedAsync(inv.InviterHouseholdId, cancellationToken);
        if (targetHousehold != null)
        {
            targetHousehold.Type = HouseholdType.Couple;
            targetHousehold.UpdatedAt = DateTime.UtcNow;
        }

        user.HouseholdId = inv.InviterHouseholdId;
        user.UpdatedAt = DateTime.UtcNow;

        inv.Status = CoupleInvitationStatus.Accepted;
        inv.AcceptedAt = DateTime.UtcNow;
        inv.UpdatedAt = DateTime.UtcNow;

        await _userRepository.UpdateAsync(user, cancellationToken);

        if (oldHouseholdId != inv.InviterHouseholdId)
        {
            var remaining = await _userRepository.GetByHouseholdIdAsync(oldHouseholdId, cancellationToken);
            if (remaining.Count == 0)
            {
                var oldH = await _householdRepository.GetByIdTrackedAsync(oldHouseholdId, cancellationToken);
                if (oldH != null)
                    await _householdRepository.DeleteAsync(oldH, cancellationToken);
            }
        }
    }

    private async Task<bool> HasPersonalHouseholdDataAsync(Guid householdId, CancellationToken cancellationToken)
    {
        var accounts = await _accountRepository.GetByHouseholdIdAsync(householdId, cancellationToken);
        if (accounts.Count > 0) return true;
        var transactions = await _transactionRepository.GetByHouseholdAsync(householdId, null, null, null, cancellationToken);
        if (transactions.Count > 0) return true;
        var recurring = await _recurringTransactionRepository.GetByHouseholdAsync(householdId, cancellationToken);
        if (recurring.Count > 0) return true;
        var objectives = await _savingsObjectiveRepository.GetByHouseholdAsync(householdId, cancellationToken);
        if (objectives.Count > 0) return true;
        var reports = await _monthlyReportRepository.ListByHouseholdAsync(householdId, null, null, cancellationToken);
        return reports.Count > 0;
    }

    private async Task MigratePersonalHouseholdDataAsync(Guid sourceHouseholdId, Guid targetHouseholdId, CancellationToken cancellationToken)
    {
        var utc = DateTime.UtcNow;

        var sourceHousehold = await _db.Households.FirstOrDefaultAsync(h => h.Id == sourceHouseholdId, cancellationToken);
        if (sourceHousehold != null)
        {
            sourceHousehold.PrimaryAccountId = null;
            sourceHousehold.UpdatedAt = utc;
            await _db.SaveChangesAsync(cancellationToken);
        }

        var targetPeriods = await _db.MonthlyReports
            .AsNoTracking()
            .Where(r => r.HouseholdId == targetHouseholdId)
            .Select(r => new { r.Year, r.Month })
            .ToListAsync(cancellationToken);
        var taken = targetPeriods.Select(x => (x.Year, x.Month)).ToHashSet();

        var sourceReports = await _db.MonthlyReports.Where(r => r.HouseholdId == sourceHouseholdId).ToListAsync(cancellationToken);
        foreach (var r in sourceReports)
        {
            if (taken.Contains((r.Year, r.Month)))
                _db.MonthlyReports.Remove(r);
            else
            {
                r.HouseholdId = targetHouseholdId;
                r.UpdatedAt = utc;
                taken.Add((r.Year, r.Month));
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        await _db.Accounts
            .Where(a => a.HouseholdId == sourceHouseholdId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.HouseholdId, targetHouseholdId)
                .SetProperty(a => a.UpdatedAt, utc), cancellationToken);

        await _db.Transactions
            .Where(t => t.HouseholdId == sourceHouseholdId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.HouseholdId, targetHouseholdId)
                .SetProperty(t => t.UpdatedAt, utc), cancellationToken);

        await _db.RecurringTransactions
            .Where(rt => rt.HouseholdId == sourceHouseholdId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(rt => rt.HouseholdId, targetHouseholdId)
                .SetProperty(rt => rt.UpdatedAt, utc), cancellationToken);

        await _db.SavingsObjectives
            .Where(o => o.HouseholdId == sourceHouseholdId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.HouseholdId, targetHouseholdId)
                .SetProperty(o => o.UpdatedAt, utc), cancellationToken);
    }

    private async Task EnsureInviteeHouseholdIsEmptyAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user?.HouseholdId == null)
            return;

        var hid = user.HouseholdId.Value;
        var members = await _userRepository.GetByHouseholdIdAsync(hid, cancellationToken);
        if (members.Count != 1)
            throw new InvalidOperationException(
                "Só podes aceitar o convite se fores o único membro do teu agregado atual.");

        var accounts = await _accountRepository.GetByHouseholdIdAsync(hid, cancellationToken);
        if (accounts.Count > 0)
            throw new InvalidOperationException(
                "Remove as contas do teu agregado atual antes de aceitar o convite (ou exporta os dados).");

        var transactions = await _transactionRepository.GetByHouseholdAsync(hid, null, null, null, cancellationToken);
        if (transactions.Count > 0)
            throw new InvalidOperationException(
                "O teu agregado atual tem movimentos. Remove-os ou exporta antes de aceitar o convite.");

        var recurring = await _recurringTransactionRepository.GetByHouseholdAsync(hid, cancellationToken);
        if (recurring.Count > 0)
            throw new InvalidOperationException(
                "O teu agregado atual tem recorrentes. Remove-as antes de aceitar o convite.");

        var objectives = await _savingsObjectiveRepository.GetByHouseholdAsync(hid, cancellationToken);
        if (objectives.Count > 0)
            throw new InvalidOperationException(
                "O teu agregado atual tem objetivos de poupança. Remove-os antes de aceitar o convite.");

        var reports = await _monthlyReportRepository.ListByHouseholdAsync(hid, null, null, cancellationToken);
        if (reports.Count > 0)
            throw new InvalidOperationException(
                "O teu agregado atual tem relatórios. Contacta o suporte se precisares de os migrar.");
    }
}
