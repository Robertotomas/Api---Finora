using Finora.Domain.Entities;

namespace Finora.Application.Interfaces;

public interface ICoupleInvitationRepository
{
    Task<CoupleInvitation?> GetByIdTrackedAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CoupleInvitation?> GetPendingByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>Pending OTP invitation matching email and OTP hash (existing-account flow).</summary>
    Task<CoupleInvitation?> GetPendingExistingAccountInviteByEmailAndOtpHashAsync(
        string inviteeEmailNormalized,
        string otpHash,
        CancellationToken cancellationToken = default);

    Task<int> CountPendingForHouseholdAsync(Guid householdId, CancellationToken cancellationToken = default);
    Task<CoupleInvitation> AddAsync(CoupleInvitation invitation, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Revoke pending invitations for this household + email before creating a new one.</summary>
    Task RevokePendingForHouseholdAndEmailAsync(Guid householdId, string inviteeEmailNormalized, CancellationToken cancellationToken = default);

    /// <summary>Revoke every pending invitation for this household (e.g. new invite replaces any previous pending).</summary>
    Task RevokeAllPendingForHouseholdAsync(Guid householdId, CancellationToken cancellationToken = default);
}
