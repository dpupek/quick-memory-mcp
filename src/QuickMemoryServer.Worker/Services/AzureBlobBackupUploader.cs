using System.Security.Cryptography;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuickMemoryServer.Worker.Configuration;

namespace QuickMemoryServer.Worker.Services;

public sealed class AzureBlobBackupUploader : IBackupArtifactUploader
{
    private const int DefaultMaxAttempts = 3;
    private static readonly int[] RetryableStatusCodes = [408, 429, 500, 502, 503, 504];

    private readonly IOptionsMonitor<ServerOptions> _optionsMonitor;
    private readonly ILogger<AzureBlobBackupUploader> _logger;

    public AzureBlobBackupUploader(IOptionsMonitor<ServerOptions> optionsMonitor, ILogger<AzureBlobBackupUploader> logger)
    {
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }

    public async Task<BackupUploadResult> TryUploadAsync(BackupArtifact artifact, CancellationToken cancellationToken)
    {
        try
        {
            var upload = _optionsMonitor.CurrentValue.Global.Backup.Upload;
            if (!upload.Enabled)
            {
                return new BackupUploadResult(false, null, "upload-disabled");
            }

            if (!TryCreateContainerClient(upload, out var containerClient, out var clientError) || containerClient is null)
            {
                return new BackupUploadResult(false, null, clientError ?? "upload-config-missing");
            }

            var localPath = artifact.LocalPath;
            if (!File.Exists(localPath))
            {
                return new BackupUploadResult(false, null, "artifact-missing");
            }

            var fileName = Path.GetFileName(localPath);
            var blobName = BackupUploadNaming.BuildBlobName(upload.Prefix, artifact.Endpoint, artifact.Mode.ToString().ToLowerInvariant(), artifact.TimestampUtc, fileName);
            var blobClient = containerClient.GetBlobClient(blobName);

            var (sha256Hex, sizeBytes) = await ComputeSha256AndSizeAsync(localPath, cancellationToken);
            var metadata = new Dictionary<string, string>
            {
                ["qms_endpoint"] = artifact.Endpoint,
                ["qms_mode"] = artifact.Mode.ToString(),
                ["qms_createdUtc"] = artifact.TimestampUtc.ToString("O"),
                ["qms_sha256"] = sha256Hex,
                ["qms_bytes"] = sizeBytes.ToString(),
                ["qms_version"] = typeof(AzureBlobBackupUploader).Assembly.GetName().Version?.ToString() ?? "unknown"
            };

            var uploadResult = await UploadWithRetryAsync(blobClient, localPath, metadata, sha256Hex, sizeBytes, cancellationToken);
            if (uploadResult.Uploaded)
            {
                return new BackupUploadResult(true, blobClient.Uri.ToString(), null);
            }

            return uploadResult with { BlobUri = null };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backup upload failed for {Endpoint} {Mode} ({Path})", artifact.Endpoint, artifact.Mode, artifact.LocalPath);
            return new BackupUploadResult(false, null, ex is RequestFailedException rfe
                ? $"azure:{rfe.Status}:{rfe.ErrorCode ?? "request-failed"}"
                : ex.Message);
        }
    }

    internal static bool TryCreateContainerClient(BackupUploadOptions upload, out BlobContainerClient? containerClient, out string? error)
    {
        containerClient = null;
        error = null;

        if (!string.Equals(upload.Provider, "azureBlob", StringComparison.OrdinalIgnoreCase))
        {
            error = "upload-provider-unsupported";
            return false;
        }

        if (!string.Equals(upload.AuthMode, "sas", StringComparison.OrdinalIgnoreCase))
        {
            error = "upload-authmode-unsupported";
            return false;
        }

        if (!OperatingSystem.IsWindows())
        {
            error = "upload-sas-platform-unsupported";
            return false;
        }

        var accountUrl = upload.AccountUrl?.Trim().TrimEnd('/');
        var container = upload.Container?.Trim();
        if (string.IsNullOrWhiteSpace(accountUrl) || string.IsNullOrWhiteSpace(container))
        {
            error = "upload-config-missing";
            return false;
        }

        if (string.IsNullOrWhiteSpace(upload.SasTokenProtected))
        {
            error = "upload-sas-missing";
            return false;
        }

        var sas = DpapiSecretProtector.UnprotectFromBase64(upload.SasTokenProtected);
        var serviceClient = new BlobServiceClient(new Uri(accountUrl), new AzureSasCredential(sas));
        containerClient = serviceClient.GetBlobContainerClient(container);
        return true;
    }

    internal static bool ShouldRetry(RequestFailedException ex)
    {
        return RetryableStatusCodes.Contains(ex.Status)
            || string.Equals(ex.ErrorCode, "ServerBusy", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ex.ErrorCode, "InternalError", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ex.ErrorCode, "OperationTimedOut", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsBlobAlreadyExists(RequestFailedException ex)
    {
        return string.Equals(ex.ErrorCode, "BlobAlreadyExists", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool ExistingBlobMatches(IDictionary<string, string> metadata, string expectedSha256Hex, long expectedBytes)
    {
        if (!metadata.TryGetValue("qms_sha256", out var sha) || string.IsNullOrWhiteSpace(sha))
        {
            return false;
        }

        if (!metadata.TryGetValue("qms_bytes", out var bytesText) || !long.TryParse(bytesText, out var bytes))
        {
            return false;
        }

        return string.Equals(sha, expectedSha256Hex, StringComparison.OrdinalIgnoreCase)
               && bytes == expectedBytes;
    }

    private static TimeSpan ComputeRetryDelay(int attempt)
    {
        // Exponential backoff with jitter: 0.5s, 1s, 2s (capped).
        var baseMs = 500;
        var maxMs = 5000;
        var expMs = baseMs * (int)Math.Pow(2, Math.Clamp(attempt - 1, 0, 10));
        var jitter = Random.Shared.Next(0, 150);
        return TimeSpan.FromMilliseconds(Math.Min(maxMs, expMs) + jitter);
    }

    private async Task<BackupUploadResult> UploadWithRetryAsync(
        BlobClient blobClient,
        string localPath,
        IDictionary<string, string> metadata,
        string sha256Hex,
        long sizeBytes,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= DefaultMaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await using var stream = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var options = new BlobUploadOptions
                {
                    Metadata = metadata,
                    Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }
                };

                await blobClient.UploadAsync(stream, options, cancellationToken);
                return new BackupUploadResult(true, blobClient.Uri.ToString(), null);
            }
            catch (RequestFailedException ex) when (IsBlobAlreadyExists(ex))
            {
                try
                {
                    var props = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);
                    if (ExistingBlobMatches(props.Value.Metadata, sha256Hex, sizeBytes))
                    {
                        return new BackupUploadResult(true, blobClient.Uri.ToString(), null);
                    }

                    return new BackupUploadResult(false, null, "upload-blob-exists-different");
                }
                catch (RequestFailedException probeEx) when (attempt < DefaultMaxAttempts && ShouldRetry(probeEx))
                {
                    // Fall through to retry delay.
                }
            }
            catch (RequestFailedException ex) when (attempt < DefaultMaxAttempts && ShouldRetry(ex))
            {
                // retry
            }
            catch (IOException) when (attempt < DefaultMaxAttempts)
            {
                // File locks / transient IO: backoff and retry.
            }

            var delay = ComputeRetryDelay(attempt);
            _logger.LogWarning("Retrying Azure upload for {Blob} after {Delay} (attempt {Attempt}/{Max})", blobClient.Name, delay, attempt, DefaultMaxAttempts);
            await Task.Delay(delay, cancellationToken);
        }

        return new BackupUploadResult(false, null, "upload-retries-exhausted");
    }

    private static async Task<(string Sha256Hex, long SizeBytes)> ComputeSha256AndSizeAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken);
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        return (hex, stream.Length);
    }
}
