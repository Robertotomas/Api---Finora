using Finora.Application.DTOs.Household;
using Finora.Application.Interfaces;
using Finora.Domain.Entities;
using Finora.Domain.Enums;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Finora.Infrastructure.Services;

public class HouseholdService : IHouseholdService
{
    private readonly ApplicationDbContext _db;
    private readonly IHouseholdRepository _householdRepository;
    private readonly IUserRepository _userRepository;
    private readonly ISubscriptionService _subscriptionService;
    private readonly IAccountRepository _accountRepository;

    public HouseholdService(
        ApplicationDbContext db,
        IHouseholdRepository householdRepository,
        IUserRepository userRepository,
        ISubscriptionService subscriptionService,
        IAccountRepository accountRepository)
    {
        _db = db;
        _householdRepository = householdRepository;
        _userRepository = userRepository;
        _subscriptionService = subscriptionService;
        _accountRepository = accountRepository;
    }

    public async Task<HouseholdDto?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null || !user.HouseholdId.HasValue || user.HouseholdId.Value != id)
            return null;

        var household = await _householdRepository.GetByIdAsync(id, cancellationToken);
        return household == null ? null : await ToDtoAsync(household, cancellationToken);
    }

    public async Task<IReadOnlyList<HouseholdMemberDto>> GetMembersAsync(Guid householdId, Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null || !user.HouseholdId.HasValue || user.HouseholdId.Value != householdId)
            return Array.Empty<HouseholdMemberDto>();

        var members = await _userRepository.GetByHouseholdIdAsync(householdId, cancellationToken);
        return members.Select(u => new HouseholdMemberDto
        {
            Id = u.Id,
            FirstName = u.FirstName,
            LastName = u.LastName,
            Email = u.Email
        }).ToList();
    }

    public async Task<HouseholdDto?> GetOrCreateForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdTrackedAsync(userId, cancellationToken);
        if (user == null)
            return null;

        if (user.HouseholdId.HasValue)
        {
            var household = await _householdRepository.GetByIdAsync(user.HouseholdId.Value, cancellationToken);
            return household == null ? null : await ToDtoAsync(household, cancellationToken);
        }

        var newHousehold = new Domain.Entities.Household
        {
            Id = Guid.NewGuid(),
            Type = HouseholdType.Individual,
            Name = $"{user.FirstName}'s Household",
            CreatedAt = DateTime.UtcNow
        };
        await _householdRepository.CreateAsync(newHousehold, cancellationToken);

        user.HouseholdId = newHousehold.Id;
        user.UpdatedAt = DateTime.UtcNow;
        await _userRepository.UpdateAsync(user, cancellationToken);

        return await ToDtoAsync(newHousehold, cancellationToken);
    }

    public async Task<HouseholdDto?> UpdateAsync(Guid id, UpdateHouseholdRequest request, Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null || !user.HouseholdId.HasValue || user.HouseholdId.Value != id)
            return null;

        var household = await _householdRepository.GetByIdTrackedAsync(id, cancellationToken);
        if (household == null)
            return null;

        household.Type = request.Type;
        household.Name = request.Name.Trim();
        household.UpdatedAt = DateTime.UtcNow;

        await _householdRepository.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(household, cancellationToken);
    }

    public async Task<HouseholdDto?> SetPrimaryAccountAsync(Guid userId, SetPrimaryAccountRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user?.HouseholdId is not { } householdId)
            return null;

        var accounts = await _accountRepository.GetByHouseholdIdAsync(householdId, cancellationToken);
        if (accounts.All(a => a.Id != request.AccountId))
            return null;

        var household = await _householdRepository.GetByIdTrackedAsync(householdId, cancellationToken);
        if (household == null)
            return null;

        household.PrimaryAccountId = request.AccountId;
        household.UpdatedAt = DateTime.UtcNow;
        await _householdRepository.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(household, cancellationToken);
    }

    public async Task LeaveCoupleHouseholdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
                ?? throw new InvalidOperationException("Utilizador não encontrado.");

            if (user.HouseholdId == null)
                throw new InvalidOperationException("Sem agregado.");

            var household = await _db.Households.FirstOrDefaultAsync(h => h.Id == user.HouseholdId, cancellationToken)
                ?? throw new InvalidOperationException("Agregado não encontrado.");

            if (household.Type != HouseholdType.Couple)
                throw new InvalidOperationException("Só podes sair quando o agregado está no plano casal.");

            var members = await _db.Users.Where(u => u.HouseholdId == household.Id).ToListAsync(cancellationToken);
            var now = DateTime.UtcNow;

            var invites = await _db.CoupleInvitations
                .Where(i => i.InviterHouseholdId == household.Id && i.Status == CoupleInvitationStatus.Pending)
                .ToListAsync(cancellationToken);
            foreach (var inv in invites)
            {
                inv.Status = CoupleInvitationStatus.Revoked;
                inv.UpdatedAt = now;
            }

            var subs = await _db.Subscriptions
                .Where(s => s.HouseholdId == household.Id && s.Status == SubscriptionStatus.Active)
                .ToListAsync(cancellationToken);
            foreach (var sub in subs)
            {
                sub.Status = SubscriptionStatus.Cancelled;
                sub.UpdatedAt = now;
            }

            household.Type = HouseholdType.Individual;
            household.UpdatedAt = now;

            if (members.Count >= 2)
            {
                var newHousehold = new Household
                {
                    Id = Guid.NewGuid(),
                    Type = HouseholdType.Individual,
                    Name = $"{user.FirstName}'s Household",
                    CreatedAt = now
                };
                _db.Households.Add(newHousehold);
                user.HouseholdId = newHousehold.Id;
                user.UpdatedAt = now;
            }
            else
            {
                user.UpdatedAt = now;
            }

            await _db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task<HouseholdDto> ToDtoAsync(Domain.Entities.Household household, CancellationToken cancellationToken)
    {
        var plan = await _subscriptionService.GetActivePlanAsync(household.Id, cancellationToken);
        return new HouseholdDto
        {
            Id = household.Id,
            Type = household.Type,
            Name = household.Name,
            CurrentPlan = (plan ?? SubscriptionPlan.Free).ToString(),
            PrimaryAccountId = household.PrimaryAccountId
        };
    }
}
