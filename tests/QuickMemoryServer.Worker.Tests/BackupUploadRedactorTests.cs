using QuickMemoryServer.Worker.Configuration;
using QuickMemoryServer.Worker.Models;

namespace QuickMemoryServer.Worker.Tests;

public sealed class BackupUploadRedactorTests
{
    [Fact]
    public void Redact_WhenSasConfigured_ReturnsConfiguredTrueAndFingerprintPrefix()
    {
#region Arrange
        var upload = new BackupUploadOptions
        {
            Enabled = true,
            Provider = "azureBlob",
            AccountUrl = "https://example.blob.core.windows.net",
            Container = "qms-backups",
            Prefix = "quick-memory",
            AuthMode = "sas",
            SasTokenProtected = "AAAA",
            SasFingerprint = "sha256:0123456789abcdef0123456789abcdef",
            SasUpdatedUtc = DateTimeOffset.Parse("2025-12-17T00:00:00Z")
        };
#endregion

#region Act
        var redacted = BackupUploadRedactor.Redact(upload);
#endregion

#region Assert
        Assert.True(redacted.Sas.Configured);
        Assert.Equal("sha256:012345678", redacted.Sas.FingerprintPrefix);
        Assert.Equal(upload.SasUpdatedUtc, redacted.Sas.UpdatedUtc);
#endregion
    }

    [Fact]
    public void Redact_WhenSasMissing_ReturnsConfiguredFalse()
    {
#region Arrange
        var upload = new BackupUploadOptions
        {
            Enabled = false,
            Provider = "azureBlob",
            AuthMode = "sas",
            SasTokenProtected = null,
            SasFingerprint = null,
            SasUpdatedUtc = null
        };
#endregion

#region Act
        var redacted = BackupUploadRedactor.Redact(upload);
#endregion

#region Assert
        Assert.False(redacted.Sas.Configured);
        Assert.Null(redacted.Sas.FingerprintPrefix);
        Assert.Null(redacted.Sas.UpdatedUtc);
#endregion
    }
}

