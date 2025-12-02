using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QuickMemoryServer.Worker.Diagnostics;
using QuickMemoryServer.Worker.Memory;

namespace QuickMemoryServer.Worker.Services;

public sealed class MemoryService : BackgroundService
{
    private readonly MemoryStoreFactory _factory;
    private readonly HealthReporter _healthReporter;
    private readonly ILogger<MemoryService> _logger;

    public MemoryService(MemoryStoreFactory factory, HealthReporter healthReporter, ILogger<MemoryService> logger)
    {
        _factory = factory;
        _healthReporter = healthReporter;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Quick Memory Server starting. Preloading stores…");
        await _factory.InitializeAllAsync(stoppingToken);
        _logger.LogInformation("Memory stores preloaded. Ready to accept MCP requests.");

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await FlushStoresAsync(stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await FlushStoresAsync(cancellationToken);
        foreach (var store in _factory.Stores.Values)
        {
            if (store is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        await base.StopAsync(cancellationToken);
    }

    private async Task FlushStoresAsync(CancellationToken cancellationToken)
    {
        foreach (var store in _factory.Stores.Values)
        {
            try
            {
                await store.PersistAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist store {Store}", store.Name);
            }
        }

        try
        {
            _healthReporter.GetReport();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Health reporting failed during scheduled flush.");
        }
    }
}
