using Prometheus;

namespace QuickMemoryServer.Worker.Diagnostics;

public sealed class ObservabilityMetrics
{
    private readonly Counter _mcpRequests;
    private readonly Histogram _mcpLatency;
    private readonly Gauge _storeEntryCount;
    private readonly Counter _backupSuccess;
    private readonly Counter _backupFailure;

    public ObservabilityMetrics()
    {
        _mcpRequests = Metrics.CreateCounter(
            "qms_mcp_requests_total",
            "Total MCP requests received",
            "endpoint",
            "command",
            "status");

        _mcpLatency = Metrics.CreateHistogram(
            "qms_mcp_request_duration_seconds",
            "Duration of MCP requests in seconds",
            "endpoint",
            "command");

        _storeEntryCount = Metrics.CreateGauge(
            "qms_store_entry_count",
            "Current entry count per store",
            "endpoint");

        _backupSuccess = Metrics.CreateCounter(
            "qms_backup_success_total",
            "Successful backups per endpoint",
            "endpoint");

        _backupFailure = Metrics.CreateCounter(
            "qms_backup_failure_total",
            "Failed backups per endpoint",
            "endpoint");
    }

    public void TrackMcpRequest(string endpoint, string command, int statusCode, double durationMilliseconds)
    {
        var endpointLabel = NormalizeLabel(endpoint);
        var commandLabel = NormalizeLabel(command);
        _mcpRequests.WithLabels(endpointLabel, commandLabel, statusCode.ToString()).Inc();
        _mcpLatency.WithLabels(endpointLabel, commandLabel).Observe(durationMilliseconds / 1000d);
    }

    public void UpdateStoreEntryCount(string endpoint, double value)
    {
        _storeEntryCount.WithLabels(NormalizeLabel(endpoint)).Set(value);
    }

    public void BackupSucceeded(string endpoint)
    {
        _backupSuccess.WithLabels(NormalizeLabel(endpoint)).Inc();
    }

    public void BackupFailed(string endpoint)
    {
        _backupFailure.WithLabels(NormalizeLabel(endpoint)).Inc();
    }

    private static string NormalizeLabel(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "unknown" : value;
    }
}
