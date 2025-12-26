using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QuickMemoryServer.Worker.Configuration;
using QuickMemoryServer.Worker.Diagnostics;

namespace QuickMemoryServer.Worker.Tests;

public sealed class HealthMetricsStoreTests
{
    [Fact]
    public void GetSnapshot_ReportsPerEndpointP95()
    {
#region Arrange
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var options = new ServerOptions
            {
                Global = new GlobalOptions
                {
                    StorageBasePath = tempDir.FullName
                }
            };

            var store = new HealthMetricsStore(new TestOptionsMonitor(options), NullLogger<HealthMetricsStore>.Instance);
            for (var i = 1; i <= 100; i++)
            {
                store.RecordSearchDuration("proj-a", i, statusCode: 200);
            }
#endregion

#region Act
            var now = DateTime.UtcNow;
            var sample = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc);
            store.CaptureAndPersistMinuteSample(sample, CancellationToken.None);
            var snapshot = store.GetSnapshot(days: 1, bucketMinutes: 60);
#endregion

#region Assert
            var current = snapshot.CurrentSearchByEndpoint["proj-a"];
            Assert.NotNull(current);
            Assert.Equal(100, current!.Count);
            Assert.Equal(0, current.ErrorCount);
            Assert.Equal(95, current.P95Ms);
#endregion
        }
        finally
        {
            try { tempDir.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void GetSnapshot_BucketsHourlyAndAggregatesCounts()
    {
#region Arrange
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var options = new ServerOptions
            {
                Global = new GlobalOptions
                {
                    StorageBasePath = tempDir.FullName
                }
            };

            var store = new HealthMetricsStore(new TestOptionsMonitor(options), NullLogger<HealthMetricsStore>.Instance);
            for (var i = 1; i <= 100; i++)
            {
                store.RecordSearchDuration("proj-a", i, statusCode: 200);
            }
            var now = DateTime.UtcNow;
            var sample1 = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);
            store.CaptureAndPersistMinuteSample(sample1, CancellationToken.None);

            for (var i = 101; i <= 200; i++)
            {
                store.RecordSearchDuration("proj-a", i, statusCode: 200);
            }
            var sample2 = sample1.AddMinutes(30);
            store.CaptureAndPersistMinuteSample(sample2, CancellationToken.None);
#endregion

#region Act
            var snapshot = store.GetSnapshot(days: 1, bucketMinutes: 60);
#endregion

#region Assert
            var series = snapshot.SearchP95SeriesByEndpoint["proj-a"];
            Assert.Single(series);
            Assert.Equal(200, series[0].Count);
            Assert.Equal(195, series[0].P95Ms);
#endregion
        }
        finally
        {
            try { tempDir.Delete(recursive: true); } catch { }
        }
    }

    private sealed class TestOptionsMonitor : IOptionsMonitor<ServerOptions>
    {
        private readonly ServerOptions _options;

        public TestOptionsMonitor(ServerOptions options)
        {
            _options = options;
        }

        public ServerOptions CurrentValue => _options;

        public ServerOptions Get(string? name) => _options;

        public IDisposable OnChange(Action<ServerOptions, string?> listener) => NullDisposable.Instance;

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
