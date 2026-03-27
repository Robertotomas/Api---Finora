using Finora.Application.DTOs.Household;
using Finora.Application.Interfaces;
using Finora.Domain.Enums;

namespace Finora.Infrastructure.Services;

public class HouseholdService : IHouseholdService
{
    private readonly IHouseholdRepository _householdRepository;
    private readonly IUserRepository _userRepository;
    private readonly ISubscriptionService _subscriptionService;
    private readonly IAccountRepository _accountRepository;

    public HouseholdService(
        IHouseholdRepository householdRepository,
        IUserRepository userRepository,
        ISubscriptionService subscriptionService,
        IAccountRepository accountRepository)
    {
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
