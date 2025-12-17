using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QuickMemoryServer.Worker.Configuration;
using QuickMemoryServer.Worker.Services;

namespace QuickMemoryServer.Worker.Tests;

public sealed class AzureBlobBackupUploaderTests
{
    [Fact]
    public async Task TryUploadAsync_WhenUploadDisabled_ReturnsUploadDisabled()
    {
#region Arrange
        var options = new ServerOptions
        {
            Global = new GlobalOptions
            {
                Backup = new BackupOptions
                {
                    Upload = new BackupUploadOptions
                    {
                        Enabled = false
                    }
                }
            }
        };

        var optionsMonitor = new TestOptionsMonitor(options);
        var uploader = new AzureBlobBackupUploader(optionsMonitor, NullLogger<AzureBlobBackupUploader>.Instance);
        var artifact = new BackupArtifact("qm-proj", BackupMode.Full, LocalPath: "C:\\temp\\backup.zip", TimestampUtc: DateTime.UtcNow);
#endregion

#region Act
        var result = await uploader.TryUploadAsync(artifact, CancellationToken.None);
#endregion

#region Assert
        Assert.False(result.Uploaded);
        Assert.Equal("upload-disabled", result.Error);
        Assert.Null(result.BlobUri);
#endregion
    }

    [Fact]
    public async Task TryUploadAsync_WhenProviderUnsupported_ReturnsProviderUnsupported()
    {
#region Arrange
        var options = new ServerOptions
        {
            Global = new GlobalOptions
            {
                Backup = new BackupOptions
                {
                    Upload = new BackupUploadOptions
                    {
                        Enabled = true,
                        Provider = "s3"
                    }
                }
            }
        };

        var optionsMonitor = new TestOptionsMonitor(options);
        var uploader = new AzureBlobBackupUploader(optionsMonitor, NullLogger<AzureBlobBackupUploader>.Instance);
        var artifact = new BackupArtifact("qm-proj", BackupMode.Full, LocalPath: "C:\\temp\\backup.zip", TimestampUtc: DateTime.UtcNow);
#endregion

#region Act
        var result = await uploader.TryUploadAsync(artifact, CancellationToken.None);
#endregion

#region Assert
        Assert.False(result.Uploaded);
        Assert.Equal("upload-provider-unsupported", result.Error);
#endregion
    }

    [Fact]
    public async Task TryUploadAsync_WhenAuthModeUnsupported_ReturnsAuthModeUnsupported()
    {
#region Arrange
        var options = new ServerOptions
        {
            Global = new GlobalOptions
            {
                Backup = new BackupOptions
                {
                    Upload = new BackupUploadOptions
                    {
                        Enabled = true,
                        Provider = "azureBlob",
                        AuthMode = "certificate"
                    }
                }
            }
        };

        var optionsMonitor = new TestOptionsMonitor(options);
        var uploader = new AzureBlobBackupUploader(optionsMonitor, NullLogger<AzureBlobBackupUploader>.Instance);
        var artifact = new BackupArtifact("qm-proj", BackupMode.Full, LocalPath: "C:\\temp\\backup.zip", TimestampUtc: DateTime.UtcNow);
#endregion

#region Act
        var result = await uploader.TryUploadAsync(artifact, CancellationToken.None);
#endregion

#region Assert
        Assert.False(result.Uploaded);
        Assert.Equal("upload-authmode-unsupported", result.Error);
#endregion
    }

    private sealed class TestOptionsMonitor : IOptionsMonitor<ServerOptions>
    {
        private readonly ServerOptions _options;

        public TestOptionsMonitor(ServerOptions options)
        {
            _options = options;
        }

        public ServerOptions CurrentValue => _options;

        public ServerOptions Get(string? name) => _options;

        public IDisposable OnChange(Action<ServerOptions, string?> listener) => NullDisposable.Instance;

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
