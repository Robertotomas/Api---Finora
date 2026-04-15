using Finora.Application.Interfaces;
using Finora.Domain.Entities;
using Finora.Domain.Enums;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Finora.Infrastructure.Repositories;

public class CoupleInvitationRepository : ICoupleInvitationRepository
{
    private readonly ApplicationDbContext _context;

    public CoupleInvitationRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<CoupleInvitation?> GetByIdTrackedAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.CoupleInvitations
            .Include(i => i.InviterHousehold)
            .Include(i => i.InviterUser)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
    }

    public async Task<CoupleInvitation?> GetPendingByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        return await _context.CoupleInvitations
            .Include(i => i.InviterHousehold)
            .Include(i => i.InviterUser)
            .AsNoTracking()
            .FirstOrDefaultAsync(i =>
                i.TokenHash == tokenHash &&
                i.Status == CoupleInvitationStatus.Pending &&
                i.Kind == CoupleInviteKind.NewAccount, cancellationToken);
    }

    public async Task<CoupleInvitation?> GetPendingExistingAccountInviteByEmailAndOtpHashAsync(
        string inviteeEmailNormalized,
        string otpHash,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        return await _context.CoupleInvitations
            .Include(i => i.InviterHousehold)
            .FirstOrDefaultAsync(i =>
                i.InviteeEmail == inviteeEmailNormalized &&
                i.Status == CoupleInvitationStatus.Pending &&
                i.Kind == CoupleInviteKind.ExistingAccount &&
                i.OtpHash == otpHash &&
                i.OtpExpiresAt != null &&
                i.OtpExpiresAt > now, cancellationToken);
    }

    public async Task<int> CountPendingForHouseholdAsync(Guid householdId, CancellationToken cancellationToken = default)
    {
        return await _context.CoupleInvitations
            .CountAsync(i => i.InviterHouseholdId == householdId && i.Status == CoupleInvitationStatus.Pending, cancellationToken);
    }

    public async Task<CoupleInvitation> AddAsync(CoupleInvitation invitation, CancellationToken cancellationToken = default)
    {
        _context.CoupleInvitations.Add(invitation);
        await _context.SaveChangesAsync(cancellationToken);
        return invitation;
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokePendingForHouseholdAndEmailAsync(Guid householdId, string inviteeEmailNormalized, CancellationToken cancellationToken = default)
    {
        var entities = await _context.CoupleInvitations
            .Where(i => i.InviterHouseholdId == householdId &&
                        i.InviteeEmail == inviteeEmailNormalized &&
                        i.Status == CoupleInvitationStatus.Pending)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var e in entities)
        {
            e.Status = CoupleInvitationStatus.Revoked;
            e.UpdatedAt = now;
        }

        if (entities.Count > 0)
            await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeAllPendingForHouseholdAsync(Guid householdId, CancellationToken cancellationToken = default)
    {
        var entities = await _context.CoupleInvitations
            .Where(i => i.InviterHouseholdId == householdId && i.Status == CoupleInvitationStatus.Pending)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var e in entities)
        {
            e.Status = CoupleInvitationStatus.Revoked;
            e.UpdatedAt = now;
        }

        if (entities.Count > 0)
            await _context.SaveChangesAsync(cancellationToken);
    }
}
