using Finora.Application.DTOs.Household;

namespace Finora.Application.Interfaces;

public interface IHouseholdService
{
    Task<HouseholdDto?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HouseholdMemberDto>> GetMembersAsync(Guid householdId, Guid userId, CancellationToken cancellationToken = default);
    Task<HouseholdDto?> GetOrCreateForUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<HouseholdDto?> UpdateAsync(Guid id, UpdateHouseholdRequest request, Guid userId, CancellationToken cancellationToken = default);
    Task<HouseholdDto?> SetPrimaryAccountAsync(Guid userId, SetPrimaryAccountRequest request, CancellationToken cancellationToken = default);

    /// <summary>Leave a Couple household: cancels subscriptions, both sides end on Free; leaver gets a new individual household when two members were present.</summary>
    Task LeaveCoupleHouseholdAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<HouseholdDto?> DismissPartnerLeftNoticeAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Deletes all accounts, transactions, recurring rules, savings objectives and monthly reports for the user's household. Irreversible.</summary>
    Task<HouseholdDto?> ResetFinancialDataAsync(Guid userId, string confirmPhrase, CancellationToken cancellationToken = default);
}
