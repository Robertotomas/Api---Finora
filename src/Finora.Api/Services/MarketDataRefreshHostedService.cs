using Finora.Application.Interfaces;

namespace Finora.Api.Services;

/// <summary>
/// Atualiza o cache de cotações 1×/dia (~22:40 UTC, depois do fecho dos mercados EUA+EU).
/// Faz também um catch-up no arranque para não ficar com preços vazios.
/// </summary>
public class MarketDataRefreshHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MarketDataRefreshHostedService> _logger;

    private static readonly TimeSpan DailyRunTimeUtc = new(22, 40, 0);

    public MarketDataRefreshHostedService(IServiceScopeFactory scopeFactory, ILogger<MarketDataRefreshHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Catch-up no arranque (curto atraso para a app estabilizar).
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
            await RefreshAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        catch (Exception ex) { _logger.LogError(ex, "Startup market data refresh failed."); }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(DelayUntilNextRun(), stoppingToken);
                await RefreshAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Market data refresh tick failed."); }
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IMarketDataRefreshService>();
        var count = await svc.RefreshAllAsync(cancellationToken);
        _logger.LogInformation("Market data refresh complete: {Count} quotes updated.", count);
    }

    private static TimeSpan DelayUntilNextRun()
    {
        var now = DateTime.UtcNow;
        var next = now.Date + DailyRunTimeUtc;
        if (next <= now) next = next.AddDays(1);
        return next - now;
    }
}
