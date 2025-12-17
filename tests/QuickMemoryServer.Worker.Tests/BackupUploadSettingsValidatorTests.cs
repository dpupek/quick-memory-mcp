using QuickMemoryServer.Worker.Models;

namespace QuickMemoryServer.Worker.Tests;

public sealed class BackupUploadSettingsValidatorTests
{
    [Fact]
    public void Validate_WhenEnabledAndSasMissing_ReturnsError()
    {
#region Arrange
        var request = new BackupUploadSettingsRequest(
            Enabled: true,
            Provider: "azureBlob",
            AccountUrl: "https://example.blob.core.windows.net",
            Container: "qms-backups",
            Prefix: "quick-memory",
            AuthMode: "sas",
            CertificateThumbprint: null);
#endregion

#region Act
        var result = BackupUploadSettingsValidator.Validate(request, hasExistingSasToken: false);
#endregion

#region Assert
        Assert.False(result.IsValid);
        Assert.Contains("upload-sas-missing", result.Errors);
#endregion
    }

    [Fact]
    public void Validate_WhenEnabledAndSasPresent_IsValid()
    {
#region Arrange
        var request = new BackupUploadSettingsRequest(
            Enabled: true,
            Provider: "azureBlob",
            AccountUrl: "https://example.blob.core.windows.net/",
            Container: "qms-backups",
            Prefix: "/quick-memory/",
            AuthMode: "sas",
            CertificateThumbprint: null);
#endregion

#region Act
        var result = BackupUploadSettingsValidator.Validate(request, hasExistingSasToken: true);
#endregion

#region Assert
        Assert.True(result.IsValid);
#endregion
    }

    [Fact]
    public void Validate_WhenEnabledAndInvalidContainer_ReturnsError()
    {
#region Arrange
        var request = new BackupUploadSettingsRequest(
            Enabled: true,
            Provider: "azureBlob",
            AccountUrl: "https://example.blob.core.windows.net",
            Container: "NOT_VALID",
            Prefix: null,
            AuthMode: "sas",
            CertificateThumbprint: null);
#endregion

#region Act
        var result = BackupUploadSettingsValidator.Validate(request, hasExistingSasToken: true);
#endregion

#region Assert
        Assert.False(result.IsValid);
        Assert.Contains("upload-container-invalid", result.Errors);
#endregion
    }

    [Fact]
    public void Normalize_ProducesTrimmedValues()
    {
#region Arrange
        var request = new BackupUploadSettingsRequest(
            Enabled: false,
            Provider: " azureBlob ",
            AccountUrl: " https://example.blob.core.windows.net/ ",
            Container: " qms-backups ",
            Prefix: " /quick-memory/ ",
            AuthMode: " sas ",
            CertificateThumbprint: "  ABCD  ");
#endregion

#region Act
        var normalized = BackupUploadSettingsValidator.Normalize(request);
#endregion

#region Assert
        Assert.Equal("azureBlob", normalized.Provider);
        Assert.Equal("https://example.blob.core.windows.net", normalized.AccountUrl);
        Assert.Equal("qms-backups", normalized.Container);
        Assert.Equal("quick-memory", normalized.Prefix);
        Assert.Equal("sas", normalized.AuthMode);
        Assert.Equal("ABCD", normalized.CertificateThumbprint);
#endregion
    }
}

