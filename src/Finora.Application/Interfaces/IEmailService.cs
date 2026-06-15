namespace Finora.Application.Interfaces;

public interface IEmailService
{
    Task SendCoupleInviteLinkAsync(string toEmail, string inviterDisplayName, string registerUrl, CancellationToken cancellationToken = default);
    Task SendCoupleInviteOtpAsync(string toEmail, string inviterDisplayName, string otpCode, CancellationToken cancellationToken = default);
    Task SendPasswordResetLinkAsync(string toEmail, string resetUrl, CancellationToken cancellationToken = default);
    Task SendEmailConfirmationLinkAsync(string toEmail, string confirmationUrl, CancellationToken cancellationToken = default);
    Task SendSubscriptionConfirmationAsync(string toEmail, string planName, string manageUrl, CancellationToken cancellationToken = default);
}
