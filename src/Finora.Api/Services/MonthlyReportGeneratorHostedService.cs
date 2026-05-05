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

        // 1) Catch-up: generate any missing reports from past months on startup
        try
        {
            _logger.LogInformation("Running startup catch-up for missing reports...");
            await using var scope = _scopeFactory.CreateAsyncScope();
            var gen = scope.ServiceProvider.GetRequiredService<IMonthlyReportGenerationService>();
            await gen.GenerateDueReportsAsync(stoppingToken);
            _logger.LogInformation("Startup catch-up complete.");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Startup catch-up failed.");
        }

        // 2) Then check once per day
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);

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
