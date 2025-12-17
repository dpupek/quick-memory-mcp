using QuickMemoryServer.Worker.Configuration;
using QuickMemoryServer.Worker.Services;

namespace QuickMemoryServer.Worker.Models;

public sealed record BackupUploadSasStatus(bool Configured, string? FingerprintPrefix, DateTimeOffset? UpdatedUtc);

public sealed record BackupUploadRedactedSettings(
    bool Enabled,
    string Provider,
    string? AccountUrl,
    string? Container,
    string? Prefix,
    string AuthMode,
    string? CertificateThumbprint,
    BackupUploadSasStatus Sas);

public static class BackupUploadRedactor
{
    public static BackupUploadRedactedSettings Redact(BackupUploadOptions upload)
    {
        ArgumentNullException.ThrowIfNull(upload);

        var configured = !string.IsNullOrWhiteSpace(upload.SasTokenProtected);
        var prefix = BackupUploadCrypto.FingerprintPrefix(upload.SasFingerprint);
        var sas = new BackupUploadSasStatus(configured, prefix, upload.SasUpdatedUtc);

        return new BackupUploadRedactedSettings(
            upload.Enabled,
            upload.Provider,
            upload.AccountUrl,
            upload.Container,
            upload.Prefix,
            upload.AuthMode,
            upload.CertificateThumbprint,
            sas);
    }
}

