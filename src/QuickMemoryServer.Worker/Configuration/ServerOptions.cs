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
    /// Endpoint permissions mapped by endpoint key -> (user -> tier).
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
}

public enum PermissionTier
{
    Reader,
    Editor,
    Curator,
    Admin
}
