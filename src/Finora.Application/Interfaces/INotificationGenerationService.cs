namespace Finora.Application.Interfaces;

public interface INotificationGenerationService
{
    Task GeneratePendingNotificationsAsync(CancellationToken cancellationToken = default);
}
