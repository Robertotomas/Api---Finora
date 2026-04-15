namespace Finora.Application.Interfaces;

public interface IEmailService
{
    Task SendCoupleInviteLinkAsync(string toEmail, string inviterDisplayName, string registerUrl, CancellationToken cancellationToken = default);
    Task SendCoupleInviteOtpAsync(string toEmail, string inviterDisplayName, string otpCode, CancellationToken cancellationToken = default);
}
