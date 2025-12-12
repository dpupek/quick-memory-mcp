using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Reflection;
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
    string Version,
    DateTime? BuildDateUtc,
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
    private readonly ConcurrentDictionary<string, string> _issues = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, BackupAttempt> _lastBackupAttempt = new(StringComparer.OrdinalIgnoreCase);

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

    public void ReportIssue(string key, string message)
    {
        _issues[key] = message;
        _logger.LogWarning("Health issue recorded for {Key}: {Message}", key, message);
    }

    public void ClearIssue(string key)
    {
        _issues.TryRemove(key, out _);
    }

    public void RecordBackupAttempt(string endpoint, string status, string message, DateTime timestampUtc)
    {
        _lastBackupAttempt[endpoint] = new BackupAttempt(status, message, timestampUtc);
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

        foreach (var (endpoint, attempt) in _lastBackupAttempt)
        {
            if (!string.Equals(attempt.Status, "Success", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add($"Backup {endpoint}: last attempt {attempt.Status} at {attempt.TimestampUtc:o} - {attempt.Message}");
            }
        }

        issues.AddRange(_issues.Values);

        foreach (var store in stores)
        {
            _metrics.UpdateStoreEntryCount(store.Endpoint, store.EntryCount);
        }

        ObservabilityEventSource.Log.ReportEntryCount(totalEntries);

        var status = issues.Count == 0 ? "Healthy" : "Degraded";
        var assembly = Assembly.GetEntryAssembly();
        var version = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? assembly?.GetName().Version?.ToString()
                      ?? "1.0.0.0";
        DateTime? buildDateUtc = null;
        var baseDir = AppContext.BaseDirectory;
        if (!string.IsNullOrWhiteSpace(baseDir))
        {
            var candidate = Path.Combine(baseDir, "QuickMemoryServer.dll");
            if (File.Exists(candidate))
            {
                buildDateUtc = File.GetCreationTimeUtc(candidate);
            }
        }
        var report = new HealthReport(
            status,
            now,
            now - _startedAt,
            version,
            buildDateUtc,
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

public sealed record BackupAttempt(string Status, string Message, DateTime TimestampUtc);
