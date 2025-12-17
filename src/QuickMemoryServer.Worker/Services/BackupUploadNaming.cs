namespace QuickMemoryServer.Worker.Services;

public static class BackupUploadNaming
{
    public static string BuildBlobName(string? prefix, string endpoint, string mode, DateTime utcDate, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(mode);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var safePrefix = string.IsNullOrWhiteSpace(prefix) ? null : prefix.Trim().Trim('/');
        var safeEndpoint = endpoint.Trim();
        var safeMode = mode.Trim();
        var safeFile = Path.GetFileName(fileName.Trim());

        var yyyy = utcDate.ToString("yyyy");
        var mm = utcDate.ToString("MM");
        var dd = utcDate.ToString("dd");

        return string.IsNullOrWhiteSpace(safePrefix)
            ? $"{safeEndpoint}/{safeMode}/{yyyy}/{mm}/{dd}/{safeFile}"
            : $"{safePrefix}/{safeEndpoint}/{safeMode}/{yyyy}/{mm}/{dd}/{safeFile}";
    }
}

