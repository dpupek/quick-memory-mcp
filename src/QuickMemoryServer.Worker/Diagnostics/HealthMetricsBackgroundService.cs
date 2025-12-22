using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace QuickMemoryServer.Worker.Diagnostics;

public sealed class HealthMetricsBackgroundService : BackgroundService
{
    private readonly HealthMetricsStore _store;
    private readonly HealthReporter _healthReporter;
    private readonly ILogger<HealthMetricsBackgroundService> _logger;

    public HealthMetricsBackgroundService(
        HealthMetricsStore store,
        HealthReporter healthReporter,
        ILogger<HealthMetricsBackgroundService> logger)
    {
        _store = store;
        _healthReporter = healthReporter;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _store.LoadFromDisk();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Health metrics history failed to load.");
        }

        var nextCompactUtc = DateTime.UtcNow.AddHours(6);

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var next = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc).AddMinutes(1);
            var delay = next - now;
            if (delay < TimeSpan.Zero)
            {
                delay = TimeSpan.Zero;
            }

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }

            var tickUtc = DateTime.UtcNow;
            tickUtc = new DateTime(tickUtc.Year, tickUtc.Month, tickUtc.Day, tickUtc.Hour, tickUtc.Minute, 0, DateTimeKind.Utc);

            try
            {
                _store.CaptureAndPersistMinuteSample(tickUtc, stoppingToken);
                _healthReporter.ClearIssue("health-metrics");
            }
            catch (Exception ex)
            {
                _healthReporter.ReportIssue("health-metrics", $"Failed to record health metrics: {ex.Message}");
                _logger.LogWarning(ex, "Failed to record health metrics sample.");
            }

            if (DateTime.UtcNow >= nextCompactUtc)
            {
                try
                {
                    _store.CompactOnDisk();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Health metrics compaction failed.");
                }

                nextCompactUtc = DateTime.UtcNow.AddHours(6);
            }
        }
    }
}

