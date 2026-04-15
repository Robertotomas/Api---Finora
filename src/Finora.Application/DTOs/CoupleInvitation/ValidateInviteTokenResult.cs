namespace Finora.Application.DTOs.CoupleInvitation;

public class ValidateInviteTokenResult
{
    public bool Valid { get; init; }
    public string? InviterName { get; init; }
    public string? HouseholdName { get; init; }
    /// <summary>Masked email (e.g. j***@mail.pt) expected at registration.</summary>
    public string? InviteeEmailMasked { get; init; }
}
