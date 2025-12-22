using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuickMemoryServer.Worker.Configuration;

namespace QuickMemoryServer.Worker.Diagnostics;

public sealed record ProcessMetricPoint(
    DateTime TimestampUtc,
    double CpuPercent,
    long WorkingSetBytes,
    long GcHeapBytes);

public sealed record SearchMetricPoint(
    DateTime TimestampUtc,
    string Endpoint,
    int Count,
    int ErrorCount,
    double AvgMs,
    double P95Ms,
    double MaxMs);

public sealed record HealthMetricsSnapshot(
    DateTime GeneratedUtc,
    int RetentionDays,
    IReadOnlyList<ProcessMetricPoint> ProcessSeries,
    IReadOnlyDictionary<string, IReadOnlyList<SearchMetricPoint>> SearchP95SeriesByEndpoint,
    ProcessMetricPoint? CurrentProcess,
    IReadOnlyDictionary<string, SearchMetricPoint?> CurrentSearchByEndpoint);

internal sealed record SearchObservation(string Endpoint, double DurationMs, bool IsError);

public sealed class HealthMetricsStore
{
    private const int RetentionDays = 14;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentQueue<SearchObservation> _searchObservations = new();
    private readonly List<ProcessMetricPoint> _process = new();
    private readonly List<SearchMetricPoint> _search = new();
    private readonly object _gate = new();

    private readonly IOptionsMonitor<ServerOptions> _optionsMonitor;
    private readonly ILogger<HealthMetricsStore> _logger;

    private DateTime _lastProcessSampleUtc = DateTime.MinValue;
    private TimeSpan _lastCpuTotal = TimeSpan.Zero;

    public HealthMetricsStore(IOptionsMonitor<ServerOptions> optionsMonitor, ILogger<HealthMetricsStore> logger)
    {
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }

    public void RecordSearchDuration(string endpoint, double durationMs, int statusCode)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            endpoint = "unknown";
        }

        var isError = statusCode >= 400;
        _searchObservations.Enqueue(new SearchObservation(endpoint, Math.Max(0, durationMs), isError));
    }

    public void CaptureAndPersistMinuteSample(DateTime nowUtc, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var processPoint = CaptureProcess(nowUtc);
        var searchPoints = DrainSearch(nowUtc);

        lock (_gate)
        {
            _process.Add(processPoint);
            _search.AddRange(searchPoints);
            TrimLocked(nowUtc);
        }

        Persist(nowUtc, processPoint, searchPoints);
    }

    public HealthMetricsSnapshot GetSnapshot(int days, int bucketMinutes)
    {
        days = Math.Clamp(days, 1, RetentionDays);
        bucketMinutes = Math.Clamp(bucketMinutes, 1, 24 * 60);

        var now = DateTime.UtcNow;
        var from = now.AddDays(-days);

        List<ProcessMetricPoint> process;
        List<SearchMetricPoint> search;
        lock (_gate)
        {
            process = _process.Where(p => p.TimestampUtc >= from).ToList();
            search = _search.Where(s => s.TimestampUtc >= from).ToList();
        }

        var bucketedProcess = BucketProcess(process, bucketMinutes);
        var bucketedSearchByEndpoint = BucketSearch(search, bucketMinutes);

        var currentProcess = bucketedProcess.LastOrDefault();
        var currentSearch = bucketedSearchByEndpoint.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.LastOrDefault(), StringComparer.OrdinalIgnoreCase);

        return new HealthMetricsSnapshot(
            now,
            RetentionDays,
            bucketedProcess,
            bucketedSearchByEndpoint,
            currentProcess,
            currentSearch);
    }

    private static DateTime BucketStartUtc(DateTime utc, int bucketMinutes)
    {
        var minute = (utc.Minute / bucketMinutes) * bucketMinutes;
        return new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, minute, 0, DateTimeKind.Utc);
    }

    private static List<ProcessMetricPoint> BucketProcess(IEnumerable<ProcessMetricPoint> points, int bucketMinutes)
    {
        return points
            .GroupBy(p => BucketStartUtc(p.TimestampUtc, bucketMinutes))
            .Select(g =>
            {
                var cpuAvg = g.Average(x => x.CpuPercent);
                var wsMax = g.Max(x => x.WorkingSetBytes);
                var gcMax = g.Max(x => x.GcHeapBytes);
                return new ProcessMetricPoint(g.Key, cpuAvg, wsMax, gcMax);
            })
            .OrderBy(p => p.TimestampUtc)
            .ToList();
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<SearchMetricPoint>> BucketSearch(IEnumerable<SearchMetricPoint> points, int bucketMinutes)
    {
        var byEndpoint = points
            .GroupBy(p => p.Endpoint, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g
                    .GroupBy(p => BucketStartUtc(p.TimestampUtc, bucketMinutes))
                    .Select(bucket =>
                    {
                        var count = bucket.Sum(x => x.Count);
                        var errors = bucket.Sum(x => x.ErrorCount);
                        var maxMs = bucket.Max(x => x.MaxMs);
                        var p95Ms = bucket.Max(x => x.P95Ms);
                        var avgMs = count > 0
                            ? bucket.Sum(x => x.AvgMs * x.Count) / count
                            : 0;
                        return new SearchMetricPoint(bucket.Key, g.Key, count, errors, avgMs, p95Ms, maxMs);
                    })
                    .OrderBy(p => p.TimestampUtc)
                    .ToList() as IReadOnlyList<SearchMetricPoint>,
                StringComparer.OrdinalIgnoreCase);

        return byEndpoint;
    }

    public void LoadFromDisk()
    {
        var path = GetMetricsFilePath();
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var now = DateTime.UtcNow;
            var threshold = now.AddDays(-RetentionDays);
            var loadedProcess = new List<ProcessMetricPoint>();
            var loadedSearch = new List<SearchMetricPoint>();

            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                using var doc = JsonDocument.Parse(line);
                if (!doc.RootElement.TryGetProperty("kind", out var kindEl))
                {
                    continue;
                }

                var kind = kindEl.GetString();
                if (string.Equals(kind, "process", StringComparison.OrdinalIgnoreCase))
                {
                    var point = JsonSerializer.Deserialize<ProcessLine>(line, Json);
                    if (point is null || point.TimestampUtc < threshold)
                    {
                        continue;
                    }

                    loadedProcess.Add(new ProcessMetricPoint(point.TimestampUtc, point.CpuPercent, point.WorkingSetBytes, point.GcHeapBytes));
                }
                else if (string.Equals(kind, "search", StringComparison.OrdinalIgnoreCase))
                {
                    var point = JsonSerializer.Deserialize<SearchLine>(line, Json);
                    if (point is null || point.TimestampUtc < threshold)
                    {
                        continue;
                    }

                    loadedSearch.Add(new SearchMetricPoint(point.TimestampUtc, point.Endpoint, point.Count, point.ErrorCount, point.AvgMs, point.P95Ms, point.MaxMs));
                }
            }

            lock (_gate)
            {
                _process.Clear();
                _search.Clear();
                _process.AddRange(loadedProcess.OrderBy(p => p.TimestampUtc));
                _search.AddRange(loadedSearch.OrderBy(s => s.TimestampUtc));
                TrimLocked(now);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load health metrics history from disk.");
        }
    }

    public void CompactOnDisk()
    {
        var path = GetMetricsFilePath();
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var now = DateTime.UtcNow;
            var threshold = now.AddDays(-RetentionDays);
            var temp = path + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            using (var writer = new StreamWriter(new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None)))
            {
                lock (_gate)
                {
                    foreach (var p in _process.Where(p => p.TimestampUtc >= threshold))
                    {
                        writer.WriteLine(JsonSerializer.Serialize(new ProcessLine("process", p.TimestampUtc, p.CpuPercent, p.WorkingSetBytes, p.GcHeapBytes), Json));
                    }

                    foreach (var s in _search.Where(s => s.TimestampUtc >= threshold))
                    {
                        writer.WriteLine(JsonSerializer.Serialize(new SearchLine("search", s.TimestampUtc, s.Endpoint, s.Count, s.ErrorCount, s.AvgMs, s.P95Ms, s.MaxMs), Json));
                    }
                }
            }

            File.Copy(temp, path, overwrite: true);
            File.Delete(temp);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to compact health metrics history.");
        }
    }

    private ProcessMetricPoint CaptureProcess(DateTime nowUtc)
    {
        var proc = Process.GetCurrentProcess();
        var totalCpu = proc.TotalProcessorTime;
        var workingSet = proc.WorkingSet64;
        var gcHeap = GC.GetTotalMemory(forceFullCollection: false);

        var cpuPercent = 0d;
        if (_lastProcessSampleUtc != DateTime.MinValue)
        {
            var elapsed = nowUtc - _lastProcessSampleUtc;
            if (elapsed > TimeSpan.Zero)
            {
                var cpuDelta = totalCpu - _lastCpuTotal;
                cpuPercent = (cpuDelta.TotalMilliseconds / (elapsed.TotalMilliseconds * Environment.ProcessorCount)) * 100d;
                cpuPercent = Math.Clamp(cpuPercent, 0, 100);
            }
        }

        _lastProcessSampleUtc = nowUtc;
        _lastCpuTotal = totalCpu;

        return new ProcessMetricPoint(nowUtc, cpuPercent, workingSet, gcHeap);
    }

    private IReadOnlyList<SearchMetricPoint> DrainSearch(DateTime nowUtc)
    {
        var drained = new List<SearchObservation>();
        while (_searchObservations.TryDequeue(out var obs))
        {
            drained.Add(obs);
        }

        if (drained.Count == 0)
        {
            return Array.Empty<SearchMetricPoint>();
        }

        var byEndpoint = drained
            .GroupBy(o => o.Endpoint, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var points = new List<SearchMetricPoint>(byEndpoint.Count);
        foreach (var (endpoint, list) in byEndpoint)
        {
            var durations = list.Select(o => o.DurationMs).OrderBy(v => v).ToArray();
            var count = durations.Length;
            if (count == 0)
            {
                continue;
            }

            var errorCount = list.Count(o => o.IsError);
            var avg = durations.Average();
            var max = durations[^1];
            var p95 = Percentile(durations, 0.95);
            points.Add(new SearchMetricPoint(nowUtc, endpoint, count, errorCount, avg, p95, max));
        }

        return points;
    }

    internal static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 0)
        {
            return 0;
        }

        p = Math.Clamp(p, 0, 1);
        var idx = (int)Math.Ceiling(p * sorted.Length) - 1;
        idx = Math.Clamp(idx, 0, sorted.Length - 1);
        return sorted[idx];
    }

    private void TrimLocked(DateTime nowUtc)
    {
        var threshold = nowUtc.AddDays(-RetentionDays);
        _process.RemoveAll(p => p.TimestampUtc < threshold);
        _search.RemoveAll(s => s.TimestampUtc < threshold);
    }

    private void Persist(DateTime nowUtc, ProcessMetricPoint process, IReadOnlyList<SearchMetricPoint> search)
    {
        try
        {
            var path = GetMetricsFilePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream);

            writer.WriteLine(JsonSerializer.Serialize(new ProcessLine("process", process.TimestampUtc, process.CpuPercent, process.WorkingSetBytes, process.GcHeapBytes), Json));
            foreach (var point in search)
            {
                writer.WriteLine(JsonSerializer.Serialize(new SearchLine("search", point.TimestampUtc, point.Endpoint, point.Count, point.ErrorCount, point.AvgMs, point.P95Ms, point.MaxMs), Json));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist health metrics sample.");
        }
    }

    private string GetMetricsFilePath()
    {
        var basePath = _optionsMonitor.CurrentValue.Global.StorageBasePath;
        if (string.IsNullOrWhiteSpace(basePath))
        {
            basePath = AppContext.BaseDirectory;
        }

        var dir = Path.Combine(basePath, "health-metrics");
        return Path.Combine(dir, "metrics.jsonl");
    }

    private sealed record ProcessLine(string Kind, DateTime TimestampUtc, double CpuPercent, long WorkingSetBytes, long GcHeapBytes);
    private sealed record SearchLine(string Kind, DateTime TimestampUtc, string Endpoint, int Count, int ErrorCount, double AvgMs, double P95Ms, double MaxMs);
}
