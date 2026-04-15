using Finora.Application.DTOs.CoupleInvitation;

namespace Finora.Application.Interfaces;

public interface ICoupleInvitationService
{
    /// <summary>Sends invite email (link or OTP). O plano passa a Couple só depois do envio bem-sucedido.</summary>
    Task CreateInvitationAsync(Guid inviterUserId, string inviteeEmail, CancellationToken cancellationToken = default);

    Task<ValidateInviteTokenResult> ValidateInviteTokenAsync(string rawToken, CancellationToken cancellationToken = default);

    /// <summary>Validates token + email before registration; does not consume the invite.</summary>
    Task<NewUserInviteContext?> PrepareNewUserInviteAsync(string emailNormalized, string rawToken, CancellationToken cancellationToken = default);

    /// <summary>After user row is created; marks invite accepted and sets household type to Couple.</summary>
    Task CompleteNewUserInviteAsync(Guid invitationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Logged-in user confirms OTP to join inviter household.
    /// If <paramref name="migratePersonalData"/> is true, moves contas/movimentos/recorrentes/objetivos from the current household into the inviter's; relatórios duplicados (mesmo mês/ano) no destino são descartados do agregado de origem.
    /// </summary>
    Task VerifyOtpAndJoinAsync(Guid userId, string otpCode, bool migratePersonalData, CancellationToken cancellationToken = default);
}
