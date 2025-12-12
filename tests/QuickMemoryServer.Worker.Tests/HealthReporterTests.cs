using System;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using QuickMemoryServer.Worker.Diagnostics;
using QuickMemoryServer.Worker.Memory;
using QuickMemoryServer.Worker.Models;
using Xunit;

namespace QuickMemoryServer.Worker.Tests;

public class HealthReporterTests
{
    [Fact]
    public void BackupFailure_Marks_Degraded_Until_Success()
    {
        var provider = new FakeStoreProvider();
        var metrics = new ObservabilityMetrics();
        var reporter = new HealthReporter(provider, metrics, NullLogger<HealthReporter>.Instance);

        reporter.RecordBackupAttempt("projectA", "Failure", "disk full", DateTime.UtcNow);
        var degraded = reporter.GetReport();
        Assert.Equal("Degraded", degraded.Status);
        Assert.Contains(degraded.Issues, i => i.Contains("projectA") && i.Contains("Failure"));

        reporter.RecordBackupAttempt("projectA", "Success", "ok", DateTime.UtcNow.AddMinutes(1));
        var healthy = reporter.GetReport();
        Assert.Equal("Healthy", healthy.Status);
        Assert.DoesNotContain(healthy.Issues, i => i.Contains("projectA"));
    }

    private sealed class FakeStoreProvider : IMemoryStoreProvider
    {
        public IReadOnlyDictionary<string, IMemoryStore> Stores { get; } =
            new Dictionary<string, IMemoryStore>(StringComparer.OrdinalIgnoreCase)
            {
                ["projectA"] = new FakeStore("projectA")
            };
    }

    private sealed class FakeStore : IMemoryStore
    {
        public FakeStore(string project) => Project = project;
        public string Project { get; }
        public string Name => Project;
        public string StoragePath => "/tmp";
        public FileInfo? EntriesFileInfo => null;
        public ValueTask InitializeAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public IReadOnlyCollection<MemoryEntry> Snapshot() => Array.Empty<MemoryEntry>();
        public ValueTask PersistAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public MemoryEntry? FindEntry(string id) => null;
        public IEnumerable<string> Related(string id, int maxHops) => Enumerable.Empty<string>();
        public ValueTask UpsertAsync(MemoryEntry entry, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask<bool> DeleteAsync(string id, bool force, CancellationToken cancellationToken) => ValueTask.FromResult(false);
    }
}
