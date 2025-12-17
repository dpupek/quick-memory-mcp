using System.IO.Compression;
using System.Reflection;
using System.Threading.Channels;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NCrontab;
using QuickMemoryServer.Worker.Configuration;
using QuickMemoryServer.Worker.Diagnostics;
using QuickMemoryServer.Worker.Memory;
using QuickMemoryServer.Worker.Models;

namespace QuickMemoryServer.Worker.Services;

public enum BackupMode
{
    Differential,
    Full
}

public sealed record BackupRequest(string Endpoint, BackupMode Mode, string? InitiatedBy);

public sealed class BackupService : BackgroundService
{
    private readonly Channel<BackupRequest> _requests = Channel.CreateUnbounded<BackupRequest>();
    private readonly MemoryStoreFactory _storeFactory;
    private readonly IOptionsMonitor<ServerOptions> _optionsMonitor;
    private readonly IBackupArtifactUploader _uploader;
    private readonly ObservabilityMetrics _metrics;
    private readonly HealthReporter _healthReporter;
    private readonly BackupActivityStore _activityStore;
    private readonly ILogger<BackupService> _logger;
    private readonly string _instanceId = $"{Environment.MachineName}:{Environment.ProcessId}";

    public BackupService(
        MemoryStoreFactory storeFactory,
        IOptionsMonitor<ServerOptions> optionsMonitor,
        IBackupArtifactUploader uploader,
        ObservabilityMetrics metrics,
        HealthReporter healthReporter,
        BackupActivityStore activityStore,
        ILogger<BackupService> logger)
    {
        _storeFactory = storeFactory;
        _optionsMonitor = optionsMonitor;
        _uploader = uploader;
        _metrics = metrics;
        _healthReporter = healthReporter;
        _activityStore = activityStore;
        _logger = logger;
    }

    public ValueTask RequestBackupAsync(string endpoint, BackupMode mode, CancellationToken cancellationToken, string? initiatedBy = null)
    {
        return _requests.Writer.WriteAsync(new BackupRequest(endpoint, mode, initiatedBy), cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var diffSchedule = CreateSchedule(_optionsMonitor.CurrentValue.Global.Backup.DifferentialCron);
        var fullSchedule = CreateSchedule(_optionsMonitor.CurrentValue.Global.Backup.FullCron);

        var diffTask = RunScheduleAsync(diffSchedule, BackupMode.Differential, stoppingToken);
        var fullTask = RunScheduleAsync(fullSchedule, BackupMode.Full, stoppingToken);
        var manualTask = ProcessManualRequestsAsync(stoppingToken);

        await Task.WhenAll(diffTask, fullTask, manualTask);
    }

    private async Task ProcessManualRequestsAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in _requests.Reader.ReadAllAsync(stoppingToken))
        {
            await ExecuteBackupAsync(request.Endpoint, request.Mode, stoppingToken, request.InitiatedBy);
        }
    }

    private async Task RunScheduleAsync(CrontabSchedule? schedule, BackupMode mode, CancellationToken stoppingToken)
    {
        if (schedule is null)
        {
            return;
        }

        var next = schedule.GetNextOccurrence(DateTime.UtcNow);
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = next - DateTime.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }

            var endpoints = _optionsMonitor.CurrentValue.Endpoints.Keys.ToArray();
            foreach (var endpoint in endpoints)
            {
                try
                {
                    await ExecuteBackupAsync(endpoint, mode, stoppingToken, "scheduler");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Scheduled {Mode} backup failed for {Endpoint}", mode, endpoint);
                }
            }

            next = schedule.GetNextOccurrence(DateTime.UtcNow);
        }
    }

    private async Task ExecuteBackupAsync(string endpoint, BackupMode mode, CancellationToken cancellationToken, string? initiatedBy = null)
    {
        var success = false;
        var stopwatch = Stopwatch.StartNew();
        string message = string.Empty;
        string? uploadStatus = null;
        string? uploadBlobUri = null;
        string? uploadError = null;
        try
        {
            var store = _storeFactory.GetOrCreate(endpoint);
            var configuredTarget = _optionsMonitor.CurrentValue.Global.Backup.TargetPath;
            var basePath = string.IsNullOrWhiteSpace(configuredTarget)
                ? AppContext.BaseDirectory
                : configuredTarget;
            var backupRoot = Path.Combine(basePath, "Backups");
            var artifactTimeUtc = DateTime.UtcNow;
            string? localArtifactPath = null;

            EnsureWritable(backupRoot);

            switch (mode)
            {
                case BackupMode.Differential:
                    var diffRoot = Path.Combine(backupRoot, "diff");
                    var diffFolder = Path.Combine(diffRoot, artifactTimeUtc.ToString("yyyyMMdd"), endpoint);
                    await CreateDifferentialBackupAsync(store, diffFolder, cancellationToken);
                    await PurgeOldBackupsAsync(diffRoot, _optionsMonitor.CurrentValue.Global.Backup.RetentionDays, mode);
                    message = $"Differential backup to {diffFolder}";
                    localArtifactPath = await CreateDifferentialArchiveAsync(backupRoot, endpoint, diffFolder, artifactTimeUtc, cancellationToken);
                    await PurgeOldBackupsAsync(Path.Combine(backupRoot, "diff-zips"), _optionsMonitor.CurrentValue.Global.Backup.RetentionDays, BackupMode.Full);
                    break;
                case BackupMode.Full:
                    var timestamp = artifactTimeUtc.ToString("yyyyMMddHHmmss");
                    var fullRoot = Path.Combine(backupRoot, "full");
                    var destFile = Path.Combine(fullRoot, $"{timestamp}-{endpoint}.zip");
                    await CreateFullBackupAsync(store, destFile, cancellationToken);
                    await PurgeOldBackupsAsync(fullRoot, _optionsMonitor.CurrentValue.Global.Backup.FullRetentionDays, mode);
                    message = $"Full backup to {destFile}";
                    localArtifactPath = destFile;
                    break;
            }

            if (!string.IsNullOrWhiteSpace(localArtifactPath))
            {
                var upload = await _uploader.TryUploadAsync(new BackupArtifact(endpoint, mode, localArtifactPath, artifactTimeUtc), cancellationToken);
                if (upload.Uploaded)
                {
                    uploadStatus = "Uploaded";
                    uploadBlobUri = upload.BlobUri;
                    _healthReporter.ClearIssue($"backup-upload:{endpoint}");
                }
                else
                {
                    uploadError = upload.Error;
                    if (string.Equals(upload.Error, "upload-disabled", StringComparison.OrdinalIgnoreCase))
                    {
                        uploadStatus = "Skipped";
                    }
                    else if (!string.IsNullOrWhiteSpace(upload.Error))
                    {
                        uploadStatus = "Failed";
                        _healthReporter.ReportIssue($"backup-upload:{endpoint}", $"Upload failed for {endpoint}: {upload.Error}");
                    }
                    else
                    {
                        uploadStatus = "Skipped";
                    }
                }
            }

            success = true;
        }
        catch (Exception ex)
        {
            _healthReporter.ReportIssue($"backup:{endpoint}", $"Last {mode} backup failed for {endpoint}: {ex.Message}");
            _metrics.BackupFailed(endpoint);
            ObservabilityEventSource.Log.ReportBackupFailure();
            RecordActivity(endpoint, mode, BackupActivityStatus.Failure, ex.Message, stopwatch.Elapsed.TotalMilliseconds, initiatedBy, uploadStatus, uploadBlobUri, uploadError);
            throw;
        }
        finally
        {
            if (success)
            {
                _healthReporter.ClearIssue($"backup:{endpoint}");
                _metrics.BackupSucceeded(endpoint);
                ObservabilityEventSource.Log.ReportBackupSuccess();
                RecordActivity(endpoint, mode, BackupActivityStatus.Success, message, stopwatch.Elapsed.TotalMilliseconds, initiatedBy, uploadStatus, uploadBlobUri, uploadError);
            }
        }
    }

    private static async Task<string> CreateDifferentialArchiveAsync(string backupRoot, string endpoint, string sourceDirectory, DateTime artifactTimeUtc, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);

        var archiveRoot = Path.Combine(backupRoot, "diff-zips");
        Directory.CreateDirectory(archiveRoot);
        var fileName = $"{artifactTimeUtc:yyyyMMddHHmmss}-{endpoint}.zip";
        var destination = Path.Combine(archiveRoot, fileName);

        // ZipFile APIs are sync; run on a worker thread.
        await Task.Run(() => ZipFile.CreateFromDirectory(sourceDirectory, destination, CompressionLevel.Fastest, includeBaseDirectory: false), cancellationToken);
        return destination;
    }

    private async Task CreateDifferentialBackupAsync(IMemoryStore store, string destinationDirectory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destinationDirectory);

        var sourceEntries = Path.Combine(store.StoragePath, "entries.jsonl");
        var destEntries = Path.Combine(destinationDirectory, "entries.jsonl");
        await CopyFileAsync(sourceEntries, destEntries, cancellationToken);

        var sourceIndexes = Path.Combine(store.StoragePath, "indexes");
        var destIndexes = Path.Combine(destinationDirectory, "indexes");
        if (Directory.Exists(sourceIndexes))
        {
            CopyDirectory(sourceIndexes, destIndexes, cancellationToken);
        }

        _logger.LogInformation("Differential backup completed for {Endpoint} => {Destination}", store.Project, destinationDirectory);
    }

    private Task CreateFullBackupAsync(IMemoryStore store, string destinationZip, CancellationToken cancellationToken)
    {
        var destinationDir = Path.GetDirectoryName(destinationZip)!;
        Directory.CreateDirectory(destinationDir);

        var tempDir = Path.Combine(Path.GetTempPath(), "qms-backup", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            CopyDirectory(store.StoragePath, tempDir, cancellationToken);
            ZipFile.CreateFromDirectory(tempDir, destinationZip, CompressionLevel.Fastest, includeBaseDirectory: false);
            _logger.LogInformation("Full backup completed for {Endpoint} => {Destination}", store.Project, destinationZip);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }

        return Task.CompletedTask;
    }

    private static async Task CopyFileAsync(string source, string destination, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using var sourceStream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        await using var destStream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        await sourceStream.CopyToAsync(destStream, cancellationToken);
    }

    private static void CopyDirectory(string sourceDir, string destinationDir, CancellationToken cancellationToken)
    {
        if (Directory.Exists(destinationDir))
        {
            Directory.Delete(destinationDir, recursive: true);
        }

        foreach (var dirPath in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(dirPath.Replace(sourceDir, destinationDir));
        }

        foreach (var filePath in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destPath = filePath.Replace(sourceDir, destinationDir);
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            File.Copy(filePath, destPath, overwrite: true);
        }
    }

    private Task PurgeOldBackupsAsync(string path, int retentionDays, BackupMode mode)
    {
        if (retentionDays <= 0 || !Directory.Exists(path))
        {
            return Task.CompletedTask;
        }

        var threshold = DateTime.UtcNow.AddDays(-retentionDays);

        if (mode == BackupMode.Full)
        {
            foreach (var file in Directory.GetFiles(path, "*.zip", SearchOption.TopDirectoryOnly))
            {
                var info = new FileInfo(file);
                if (info.CreationTimeUtc < threshold)
                {
                    info.Delete();
                }
            }
        }
        else
        {
            foreach (var directory in Directory.GetDirectories(path))
            {
                var info = new DirectoryInfo(directory);
                if (info.CreationTimeUtc < threshold)
                {
                    info.Delete(true);
                }
            }
        }

        return Task.CompletedTask;
    }

    private static CrontabSchedule? CreateSchedule(string cron)
    {
        if (string.IsNullOrWhiteSpace(cron))
        {
            return null;
        }

        try
        {
            return CrontabSchedule.Parse(cron, new CrontabSchedule.ParseOptions { IncludingSeconds = false });
        }
        catch
        {
            return null;
        }
    }

    private void EnsureWritable(string backupRoot)
    {
        var issueKey = $"backup-target:{backupRoot}";
        try
        {
            Directory.CreateDirectory(backupRoot);
            var probePath = Path.Combine(backupRoot, ".write-probe");
            using (var probe = new FileStream(probePath, FileMode.Create, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
                probe.WriteByte(0);
            }

            _healthReporter.ClearIssue(issueKey);
        }
        catch (Exception ex)
        {
            _healthReporter.ReportIssue(issueKey, $"Backup target '{backupRoot}' is not writable: {ex.Message}");
            _logger.LogError(ex, "Backup target {BackupRoot} is not writable", backupRoot);
            throw;
        }
    }

    private void RecordActivity(string endpoint, BackupMode mode, BackupActivityStatus status, string message, double durationMs, string? initiatedBy, string? uploadStatus, string? uploadBlobUri, string? uploadError)
    {
        var activity = new BackupActivity(
            DateTime.UtcNow,
            endpoint,
            mode,
            status,
            message,
            durationMs,
            initiatedBy,
            _instanceId,
            uploadStatus,
            uploadBlobUri,
            uploadError);

        _activityStore.Record(activity);
        _healthReporter.RecordBackupAttempt(endpoint, status.ToString(), message, activity.TimestampUtc);
    }
}
