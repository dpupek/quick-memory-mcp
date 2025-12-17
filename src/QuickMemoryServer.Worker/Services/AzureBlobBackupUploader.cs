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

            if (!string.Equals(upload.Provider, "azureBlob", StringComparison.OrdinalIgnoreCase))
            {
                return new BackupUploadResult(false, null, "upload-provider-unsupported");
            }

            if (!string.Equals(upload.AuthMode, "sas", StringComparison.OrdinalIgnoreCase))
            {
                return new BackupUploadResult(false, null, "upload-authmode-unsupported");
            }

            if (!OperatingSystem.IsWindows())
            {
                return new BackupUploadResult(false, null, "upload-sas-platform-unsupported");
            }

            var accountUrl = upload.AccountUrl?.Trim().TrimEnd('/');
            var container = upload.Container?.Trim();
            if (string.IsNullOrWhiteSpace(accountUrl) || string.IsNullOrWhiteSpace(container))
            {
                return new BackupUploadResult(false, null, "upload-config-missing");
            }

            if (string.IsNullOrWhiteSpace(upload.SasTokenProtected))
            {
                return new BackupUploadResult(false, null, "upload-sas-missing");
            }

            var sas = DpapiSecretProtector.UnprotectFromBase64(upload.SasTokenProtected);
            var serviceClient = new BlobServiceClient(new Uri(accountUrl), new AzureSasCredential(sas));
            var containerClient = serviceClient.GetBlobContainerClient(container);

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

            await using var stream = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var options = new BlobUploadOptions
            {
                Metadata = metadata
            };

            await blobClient.UploadAsync(stream, options, cancellationToken);
            return new BackupUploadResult(true, blobClient.Uri.ToString(), null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backup upload failed for {Endpoint} {Mode} ({Path})", artifact.Endpoint, artifact.Mode, artifact.LocalPath);
            return new BackupUploadResult(false, null, ex.Message);
        }
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
