using QuickMemoryServer.Worker.Services;

namespace QuickMemoryServer.Worker.Tests;

public sealed class BackupUploadCryptoTests
{
    [Fact]
    public void ComputeSha256Fingerprint_HasPrefix()
    {
#region Arrange
        const string input = "hello";
#endregion

#region Act
        var fingerprint = BackupUploadCrypto.ComputeSha256Fingerprint(input);
#endregion

#region Assert
        Assert.StartsWith("sha256:", fingerprint);
        Assert.True(fingerprint.Length > 16);
#endregion
    }

    [Fact]
    public void FingerprintPrefix_Truncates()
    {
#region Arrange
        const string fingerprint = "sha256:0123456789abcdef";
#endregion

#region Act
        var prefix = BackupUploadCrypto.FingerprintPrefix(fingerprint, length: 10);
#endregion

#region Assert
        Assert.Equal("sha256:012", prefix);
#endregion
    }
}

