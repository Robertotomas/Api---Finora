using Finora.Application.Interfaces;

namespace Finora.Api.Services;

public class MonthlyReportGeneratorHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MonthlyReportGeneratorHostedService> _logger;

    public MonthlyReportGeneratorHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<MonthlyReportGeneratorHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let migrations and the rest of the app finish starting.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                await using var scope = _scopeFactory.CreateAsyncScope();
                var gen = scope.ServiceProvider.GetRequiredService<IMonthlyReportGenerationService>();
                await gen.GenerateDueReportsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Monthly report generation tick failed.");
            }
        }
    }
}
