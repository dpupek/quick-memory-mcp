using QuickMemoryServer.Worker.Services;

namespace QuickMemoryServer.Worker.Tests;

public sealed class BackupUploadNamingTests
{
    [Fact]
    public void BuildBlobName_TrimsPrefixAndUsesDateFolders()
    {
#region Arrange
        var utc = new DateTime(2025, 12, 17, 1, 2, 3, DateTimeKind.Utc);
#endregion

#region Act
        var name = BackupUploadNaming.BuildBlobName(" /quick-memory/ ", "projectA", "full", utc, "20251217010203-projectA.zip");
#endregion

#region Assert
        Assert.Equal("quick-memory/projectA/full/2025/12/17/20251217010203-projectA.zip", name);
#endregion
    }

    [Fact]
    public void BuildBlobName_WhenPrefixMissing_DoesNotStartWithSlash()
    {
#region Arrange
        var utc = new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc);
#endregion

#region Act
        var name = BackupUploadNaming.BuildBlobName(null, "proj", "diff", utc, "file.zip");
#endregion

#region Assert
        Assert.Equal("proj/diff/2025/01/02/file.zip", name);
#endregion
    }
}

