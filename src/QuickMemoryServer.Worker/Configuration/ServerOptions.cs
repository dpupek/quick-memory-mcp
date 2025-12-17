using System.ComponentModel.DataAnnotations;

namespace QuickMemoryServer.Worker.Configuration;

/// <summary>
/// Root configuration object bound from <c>QuickMemoryServer.toml</c> or equivalent.
/// Mirrors the structure defined in the specification.
/// </summary>
public sealed class ServerOptions : IValidatableObject
{
    public GlobalOptions Global { get; set; } = new();

    public Dictionary<string, EndpointOptions> Endpoints { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, UserOptions> Users { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Project permissions mapped by project key -> (user -> tier).
    /// </summary>
    public Dictionary<string, Dictionary<string, PermissionTier>> Permissions { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Global.ServiceName))
        {
            yield return new ValidationResult("Global:ServiceName is required.");
        }

        foreach (var (key, endpoint) in Endpoints)
        {
            if (string.IsNullOrWhiteSpace(endpoint.StoragePath))
            {
                yield return new ValidationResult($"Endpoint '{key}' is missing a storagePath.");
            }

            if (string.IsNullOrWhiteSpace(endpoint.Slug))
            {
                yield return new ValidationResult($"Endpoint '{key}' is missing a slug.");
            }
        }

        foreach (var (endpointKey, assignments) in Permissions)
        {
            if (!Endpoints.ContainsKey(endpointKey))
            {
                yield return new ValidationResult($"Permissions reference unknown endpoint '{endpointKey}'.");
            }

            foreach (var userKey in assignments.Keys)
            {
                if (!Users.ContainsKey(userKey))
                {
                    yield return new ValidationResult($"Endpoint '{endpointKey}' permission references unknown user '{userKey}'.");
                }
            }
        }
    }
}

public sealed class GlobalOptions
{
    public string ServiceName { get; set; } = "QuickMemoryServer";

    public string HttpUrl { get; set; } = "http://localhost:5080";

    public string EmbeddingModel { get; set; } = "sentence-transformers/all-MiniLM-L6-v2";

    public int EmbeddingDims { get; set; } = 384;

    public string SummaryModel { get; set; } = "philschmid/bart-large-cnn-samsum";

    public BackupOptions Backup { get; set; } = new();

    public string InstallLayoutManifest { get; set; } = "layout.json";

    public string StorageBasePath { get; set; } = OperatingSystem.IsWindows()
        ? "C\\ProgramData\\q-memory-mcp"
        : "/var/lib/q-memory-mcp";
}

public sealed class EndpointOptions
{
    public string Slug { get; set; } = "";

    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    public string StoragePath { get; set; } = "";

    public bool InheritShared { get; set; } = true;

    public bool IncludeInSearchByDefault { get; set; } = true;
}

public sealed class UserOptions
{
    public string ApiKey { get; set; } = "";

    public PermissionTier DefaultTier { get; set; } = PermissionTier.Reader;
}

public sealed class BackupOptions
{
    public string DifferentialCron { get; set; } = "0 1 * * *";

    public string FullCron { get; set; } = "0 3 * * Sun";

    public int RetentionDays { get; set; } = 30;

    public int FullRetentionDays { get; set; } = 90;

    /// <summary>
    /// Optional override for where backups are written. Can be a local or UNC path.
    /// When empty, defaults to the application base directory (AppContext.BaseDirectory).
    /// </summary>
    public string? TargetPath { get; set; }

    public BackupUploadOptions Upload { get; set; } = new();
}

public sealed class BackupUploadOptions
{
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Upload provider. Currently supported: "azureBlob".
    /// </summary>
    public string Provider { get; set; } = "azureBlob";

    /// <summary>
    /// Storage account blob endpoint URL (e.g., https://{account}.blob.core.windows.net).
    /// </summary>
    public string? AccountUrl { get; set; }

    public string? Container { get; set; }

    /// <summary>
    /// Optional blob prefix for uploaded artifacts (e.g., "quick-memory").
    /// </summary>
    public string? Prefix { get; set; }

    /// <summary>
    /// Auth mode. Supported: "sas", "certificate" (future).
    /// </summary>
    public string AuthMode { get; set; } = "sas";

    /// <summary>
    /// SAS token encrypted/protected for the current machine. Never return this to clients.
    /// </summary>
    public string? SasTokenProtected { get; set; }

    /// <summary>
    /// SHA256 fingerprint of the SAS token (non-secret) for status and audits.
    /// </summary>
    public string? SasFingerprint { get; set; }

    public DateTimeOffset? SasUpdatedUtc { get; set; }

    /// <summary>
    /// Optional Windows certificate thumbprint for future certificate-based auth.
    /// </summary>
    public string? CertificateThumbprint { get; set; }

    public DateTimeOffset? CertificateUpdatedUtc { get; set; }
}

public enum PermissionTier
{
    Reader,
    Editor,
    Curator,
    Admin
}
