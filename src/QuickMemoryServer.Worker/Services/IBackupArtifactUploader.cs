namespace QuickMemoryServer.Worker.Services;

public sealed record BackupArtifact(
    string Endpoint,
    BackupMode Mode,
    string LocalPath,
    DateTime TimestampUtc);

public sealed record BackupUploadResult(
    bool Uploaded,
    string? BlobUri,
    string? Error);

public interface IBackupArtifactUploader
{
    Task<BackupUploadResult> TryUploadAsync(BackupArtifact artifact, CancellationToken cancellationToken);
}

