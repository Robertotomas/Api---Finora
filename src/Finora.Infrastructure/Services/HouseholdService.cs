using Finora.Application.DTOs.Household;
using Finora.Application.Interfaces;
using Finora.Domain.Enums;

namespace Finora.Infrastructure.Services;

public class HouseholdService : IHouseholdService
{
    private readonly IHouseholdRepository _householdRepository;
    private readonly IUserRepository _userRepository;

    public HouseholdService(IHouseholdRepository householdRepository, IUserRepository userRepository)
    {
        _householdRepository = householdRepository;
        _userRepository = userRepository;
    }

    public async Task<HouseholdDto?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null || !user.HouseholdId.HasValue || user.HouseholdId.Value != id)
            return null;

        var household = await _householdRepository.GetByIdAsync(id, cancellationToken);
        return household == null ? null : ToDto(household);
    }

    public async Task<HouseholdDto?> GetOrCreateForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdTrackedAsync(userId, cancellationToken);
        if (user == null)
            return null;

        if (user.HouseholdId.HasValue)
        {
            var household = await _householdRepository.GetByIdAsync(user.HouseholdId.Value, cancellationToken);
            return household == null ? null : ToDto(household);
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

        return ToDto(newHousehold);
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
        return ToDto(household);
    }

    private static HouseholdDto ToDto(Domain.Entities.Household household)
    {
        return new HouseholdDto
        {
            Id = household.Id,
            Type = household.Type,
            Name = household.Name
        };
    }
}
