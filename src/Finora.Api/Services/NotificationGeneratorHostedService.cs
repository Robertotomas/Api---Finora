using Finora.Application.Interfaces;

namespace Finora.Api.Services;

public class NotificationGeneratorHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NotificationGeneratorHostedService> _logger;

    public NotificationGeneratorHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<NotificationGeneratorHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        // Catch-up on startup
        try
        {
            _logger.LogInformation("Running startup notification generation...");
            await using var scope = _scopeFactory.CreateAsyncScope();
            var svc = scope.ServiceProvider.GetRequiredService<INotificationGenerationService>();
            await svc.GeneratePendingNotificationsAsync(stoppingToken);
            _logger.LogInformation("Startup notification generation complete.");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Startup notification generation failed.");
        }

        // Then run every 4 hours
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromHours(4), stoppingToken);

                await using var scope = _scopeFactory.CreateAsyncScope();
                var svc = scope.ServiceProvider.GetRequiredService<INotificationGenerationService>();
                await svc.GeneratePendingNotificationsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Notification generation tick failed.");
            }
        }
    }
}
