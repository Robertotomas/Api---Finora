namespace Finora.Application.DTOs.CoupleInvitation;

/// <summary>Validated new-account invite; use to create the user in the inviter household.</summary>
public class NewUserInviteContext
{
    public Guid InvitationId { get; init; }
    public Guid TargetHouseholdId { get; init; }
}
