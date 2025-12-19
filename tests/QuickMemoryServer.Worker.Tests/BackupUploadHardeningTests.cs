using Azure;
using QuickMemoryServer.Worker.Services;

namespace QuickMemoryServer.Worker.Tests;

public sealed class BackupUploadHardeningTests
{
    [Fact]
    public void ExistingBlobMatches_WhenShaAndBytesMatch_ReturnsTrue()
    {
#region Arrange
        var meta = new Dictionary<string, string>
        {
            ["qms_sha256"] = "abcdef",
            ["qms_bytes"] = "123"
        };
#endregion

#region Act
        var matches = AzureBlobBackupUploader.ExistingBlobMatches(meta, expectedSha256Hex: "ABCDEF", expectedBytes: 123);
#endregion

#region Assert
        Assert.True(matches);
#endregion
    }

    [Fact]
    public void ExistingBlobMatches_WhenMissingKeys_ReturnsFalse()
    {
#region Arrange
        var meta = new Dictionary<string, string>();
#endregion

#region Act
        var matches = AzureBlobBackupUploader.ExistingBlobMatches(meta, expectedSha256Hex: "abc", expectedBytes: 1);
#endregion

#region Assert
        Assert.False(matches);
#endregion
    }

    [Theory]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public void ShouldRetry_WhenRetryableStatus_ReturnsTrue(int status)
    {
#region Arrange
        var ex = new RequestFailedException(status, "fail", errorCode: "ServerBusy", innerException: null);
#endregion

#region Act
        var retry = AzureBlobBackupUploader.ShouldRetry(ex);
#endregion

#region Assert
        Assert.True(retry);
#endregion
    }

    [Fact]
    public void IsBlobAlreadyExists_WhenErrorCodeMatches_ReturnsTrue()
    {
#region Arrange
        var ex = new RequestFailedException(409, "exists", errorCode: "BlobAlreadyExists", innerException: null);
#endregion

#region Act
        var result = AzureBlobBackupUploader.IsBlobAlreadyExists(ex);
#endregion

#region Assert
        Assert.True(result);
#endregion
    }
}

