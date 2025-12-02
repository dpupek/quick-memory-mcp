using System;
using System.Diagnostics.Tracing;

namespace QuickMemoryServer.Worker.Diagnostics;

internal sealed class ObservabilityEventSource : EventSource
{
    public static readonly ObservabilityEventSource Log = new();

    private readonly EventCounter _mcpRequests;
    private readonly EventCounter _mcpLatency;
    private readonly EventCounter _entryCount;
    private readonly EventCounter _backupSuccesses;
    private readonly EventCounter _backupFailures;
    private readonly PollingCounter _managedMemory;

    private ObservabilityEventSource()
        : base("QuickMemoryServer.Observability")
    {
        _mcpRequests = new EventCounter("mcp-requests", this)
        {
            DisplayName = "MCP requests",
            DisplayUnits = "requests"
        };

        _mcpLatency = new EventCounter("mcp-request-latency-ms", this)
        {
            DisplayName = "MCP request latency",
            DisplayUnits = "ms"
        };

        _entryCount = new EventCounter("store-entry-count", this)
        {
            DisplayName = "Store entries",
            DisplayUnits = "entries"
        };

        _backupSuccesses = new EventCounter("backup-success-count", this)
        {
            DisplayName = "Successful backups",
            DisplayUnits = "backups"
        };

        _backupFailures = new EventCounter("backup-failure-count", this)
        {
            DisplayName = "Failed backups",
            DisplayUnits = "backups"
        };

        _managedMemory = new PollingCounter("managed-memory-bytes", this, () => GC.GetTotalMemory(false))
        {
            DisplayName = "Managed heap",
            DisplayUnits = "bytes"
        };
    }

    public void RecordMcpRequest(double milliseconds)
    {
        _mcpRequests.WriteMetric(1);
        _mcpLatency.WriteMetric(milliseconds);
    }

    public void ReportEntryCount(int count) => _entryCount.WriteMetric(count);

    public void ReportBackupSuccess() => _backupSuccesses.WriteMetric(1);

    public void ReportBackupFailure() => _backupFailures.WriteMetric(1);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _mcpRequests.Dispose();
            _mcpLatency.Dispose();
            _entryCount.Dispose();
            _backupSuccesses.Dispose();
            _backupFailures.Dispose();
            _managedMemory.Dispose();
        }

        base.Dispose(disposing);
    }
}
