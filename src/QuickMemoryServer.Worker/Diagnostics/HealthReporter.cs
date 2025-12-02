using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using QuickMemoryServer.Worker.Memory;

namespace QuickMemoryServer.Worker.Diagnostics;

public sealed record HealthStoreSnapshot(
    string Endpoint,
    string Name,
    string Project,
    string StoragePath,
    int EntryCount,
    long? FileSizeBytes,
    DateTime? FileLastUpdatedUtc);

public sealed record HealthReport(
    string Status,
    DateTime TimestampUtc,
    TimeSpan Uptime,
    IReadOnlyList<HealthStoreSnapshot> Stores,
    int TotalEntries,
    long TotalBytes,
    IReadOnlyList<string> Issues);

public sealed class HealthReporter
{
    private readonly IMemoryStoreProvider _storeProvider;
    private readonly ObservabilityMetrics _metrics;
    private readonly ILogger<HealthReporter> _logger;
    private readonly DateTime _startedAt;

    public HealthReporter(
        IMemoryStoreProvider storeProvider,
        ObservabilityMetrics metrics,
        ILogger<HealthReporter> logger)
    {
        _storeProvider = storeProvider;
        _metrics = metrics;
        _logger = logger;
        _startedAt = DateTime.UtcNow;
    }

    public HealthReport GetReport()
    {
        var now = DateTime.UtcNow;
        var stores = _storeProvider.Stores
            .Select(kvp => CreateSnapshot(kvp.Key, kvp.Value))
            .ToList();

        var totalEntries = stores.Sum(s => s.EntryCount);
        var totalBytes = stores.Sum(s => s.FileSizeBytes ?? 0);
        var issues = new List<string>();

        if (stores.Count == 0)
        {
            issues.Add("No endpoints configured.");
        }

        foreach (var store in stores)
        {
            _metrics.UpdateStoreEntryCount(store.Endpoint, store.EntryCount);
        }

        ObservabilityEventSource.Log.ReportEntryCount(totalEntries);

        var status = issues.Count == 0 ? "Healthy" : "Degraded";
        var report = new HealthReport(
            status,
            now,
            now - _startedAt,
            stores,
            totalEntries,
            totalBytes,
            issues.ToArray());

        if (issues.Count > 0)
        {
            _logger.LogWarning("Health report emitted {Status} with {IssueCount} issue(s): {Issues}", status, issues.Count, string.Join("; ", issues));
        }
        else
        {
            _logger.LogInformation("Health report emitted {Status} with {StoreCount} stores and {TotalEntries} entries.", status, stores.Count, totalEntries);
        }

        return report;
    }

    private static HealthStoreSnapshot CreateSnapshot(string endpoint, IMemoryStore store)
    {
        var snapshot = store.Snapshot();
        var fileInfo = store.EntriesFileInfo;
        return new HealthStoreSnapshot(
            endpoint,
            store.Name,
            store.Project,
            store.StoragePath,
            snapshot.Count,
            fileInfo?.Length,
            fileInfo?.LastWriteTimeUtc);
    }
}
