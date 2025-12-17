using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Tomlyn;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuickMemoryServer.Worker.Models;
using QuickMemoryServer.Worker.Services;

namespace QuickMemoryServer.Worker.Configuration;

public sealed class AdminConfigService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly IConfigurationRoot _configurationRoot;
    private readonly IOptionsMonitor<ServerOptions> _optionsMonitor;
    private readonly ILogger<AdminConfigService> _logger;
    private readonly string _configPath;
    private readonly object _sync = new();

    public AdminConfigService(
        IConfiguration configuration,
        IOptionsMonitor<ServerOptions> optionsMonitor,
        ILogger<AdminConfigService> logger)
    {
        _configurationRoot = configuration as IConfigurationRoot
            ?? throw new InvalidOperationException("Configuration must be an IConfigurationRoot to persist changes.");

        _optionsMonitor = optionsMonitor;
        _logger = logger;
        _configPath = Path.Combine(AppContext.BaseDirectory, "QuickMemoryServer.toml");
    }

    public IReadOnlyDictionary<string, UserOptions> ListUsers() => _optionsMonitor.CurrentValue.Users;

    public IReadOnlyDictionary<string, EndpointOptions> ListEndpoints() => _optionsMonitor.CurrentValue.Endpoints;

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, PermissionTier>> ListPermissions()
    {
        return _optionsMonitor.CurrentValue.Permissions.ToDictionary(
            permission => permission.Key,
            permission => (IReadOnlyDictionary<string, PermissionTier>)permission.Value);
    }

    public Task AddOrUpdateUserAsync(string username, UserOptions user, CancellationToken cancellationToken = default)
    {
        return MutateAsync(options =>
        {
            options.Users[username] = user;
        }, cancellationToken);
    }

    public Task UpdateBackupSettingsAsync(BackupSettingsRequest request, CancellationToken cancellationToken = default)
    {
        return MutateAsync(options =>
        {
            options.Global.Backup.DifferentialCron = request.DifferentialCron;
            options.Global.Backup.FullCron = request.FullCron;
            options.Global.Backup.RetentionDays = request.RetentionDays;
            options.Global.Backup.FullRetentionDays = request.FullRetentionDays;
            options.Global.Backup.TargetPath = string.IsNullOrWhiteSpace(request.TargetPath) ? null : request.TargetPath.Trim();
        }, cancellationToken);
    }

    public Task UpdateBackupUploadSettingsAsync(BackupUploadSettingsRequest request, CancellationToken cancellationToken = default)
    {
        return MutateAsync(options =>
        {
            var upload = options.Global.Backup.Upload;
            upload.Enabled = request.Enabled;

            var normalized = BackupUploadSettingsValidator.Normalize(request);
            upload.Provider = normalized.Provider;
            upload.AccountUrl = normalized.AccountUrl;
            upload.Container = normalized.Container;
            upload.Prefix = normalized.Prefix;
            upload.AuthMode = normalized.AuthMode;
            upload.CertificateThumbprint = normalized.CertificateThumbprint;
            if (upload.CertificateThumbprint is not null)
            {
                upload.CertificateUpdatedUtc = DateTimeOffset.UtcNow;
            }
        }, cancellationToken);
    }

    public Task SetBackupUploadSasTokenAsync(string sasToken, CancellationToken cancellationToken = default)
    {
        return MutateAsync(options =>
        {
            var token = (sasToken ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new InvalidOperationException("sas-token-missing");
            }

            var protectedToken = DpapiSecretProtector.ProtectToBase64(token);
            var fingerprint = BackupUploadCrypto.ComputeSha256Fingerprint(token);
            options.Global.Backup.Upload.SasTokenProtected = protectedToken;
            options.Global.Backup.Upload.SasFingerprint = fingerprint;
            options.Global.Backup.Upload.SasUpdatedUtc = DateTimeOffset.UtcNow;
        }, cancellationToken);
    }

    public Task ClearBackupUploadSasTokenAsync(CancellationToken cancellationToken = default)
    {
        return MutateAsync(options =>
        {
            options.Global.Backup.Upload.SasTokenProtected = null;
            options.Global.Backup.Upload.SasFingerprint = null;
            options.Global.Backup.Upload.SasUpdatedUtc = null;
        }, cancellationToken);
    }

    public Task AddOrUpdateEndpointAsync(string key, EndpointOptions endpoint, CancellationToken cancellationToken = default)
    {
        return MutateAsync(options =>
        {
            if (string.IsNullOrWhiteSpace(endpoint.Slug))
            {
                endpoint.Slug = key;
            }

            if (string.IsNullOrWhiteSpace(endpoint.StoragePath))
            {
                var baseDir = _optionsMonitor.CurrentValue.Global.StorageBasePath;
                endpoint.StoragePath = Path.Combine(baseDir, key);
            }

            options.Endpoints[key] = endpoint;
        }, cancellationToken);
    }

    public Task RemoveUserAsync(string username, CancellationToken cancellationToken = default)
    {
        return MutateAsync(options =>
        {
            options.Users.Remove(username);
            foreach (var endpointPermissions in options.Permissions.Values)
            {
                endpointPermissions.Remove(username);
            }
        }, cancellationToken);
    }

    public Task RemoveEndpointAsync(string key, CancellationToken cancellationToken = default)
    {
        return MutateAsync(options =>
        {
            options.Endpoints.Remove(key);
            options.Permissions.Remove(key);
        }, cancellationToken);
    }

    public Task SetPermissionsAsync(string endpoint, IDictionary<string, PermissionTier> assignments, CancellationToken cancellationToken = default)
    {
        return MutateAsync(options =>
        {
            if (assignments.Count == 0)
            {
                EnsureProjectHasAdmin(options, endpoint, null);
                options.Permissions.Remove(endpoint);
                return;
            }

            var normalized = new Dictionary<string, PermissionTier>(assignments, StringComparer.OrdinalIgnoreCase);
            EnsureProjectHasAdmin(options, endpoint, normalized);
            options.Permissions[endpoint] = normalized;
        }, cancellationToken);
    }

    public Task ApplyPermissionOverridesAsync(IEnumerable<string> endpoints, IDictionary<string, PermissionTier?> overrides, CancellationToken cancellationToken = default)
    {
        return MutateAsync(options =>
        {
            foreach (var endpoint in endpoints.Where(key => !string.IsNullOrWhiteSpace(key)))
            {
                if (!options.Endpoints.ContainsKey(endpoint))
                {
                    continue;
                }

                var current = options.Permissions.TryGetValue(endpoint, out var existing)
                    ? new Dictionary<string, PermissionTier>(existing, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, PermissionTier>(StringComparer.OrdinalIgnoreCase);

                foreach (var (user, tier) in overrides)
                {
                    if (string.IsNullOrWhiteSpace(user))
                    {
                        continue;
                    }

                    if (tier is null)
                    {
                        current.Remove(user);
                    }
                    else
                    {
                        current[user] = tier.Value;
                    }
                }

                if (current.Count == 0)
                {
                    EnsureProjectHasAdmin(options, endpoint, null);
                    options.Permissions.Remove(endpoint);
                }
                else
                {
                    EnsureProjectHasAdmin(options, endpoint, current);
                    options.Permissions[endpoint] = current;
                }
            }
        }, cancellationToken);
    }

    public Task<string> ReadRawAsync(CancellationToken cancellationToken = default)
    {
        return File.ReadAllTextAsync(_configPath, cancellationToken);
    }

    public Task<ConfigValidationResult> ValidateRawAsync(string content, CancellationToken cancellationToken = default)
    {
        if (content is null)
        {
            return Task.FromResult(new ConfigValidationResult(false, new[] { "content-null" }));
        }

        var model = Toml.Parse(content);
        if (!model.HasErrors)
        {
            return Task.FromResult(new ConfigValidationResult(true, Array.Empty<string>()));
        }

        var errors = model.Diagnostics.Select(d => d.ToString()).ToArray();
        return Task.FromResult(new ConfigValidationResult(false, errors));
    }

    public async Task<ConfigValidationResult> SaveRawAsync(string content, CancellationToken cancellationToken = default)
    {
        var validation = await ValidateRawAsync(content, cancellationToken);
        if (!validation.IsValid)
        {
            return validation;
        }

        await File.WriteAllTextAsync(_configPath, content, Encoding.UTF8, cancellationToken);
        _configurationRoot.Reload();
        _logger.LogInformation("Persisted raw configuration to {Path}", _configPath);
        return validation;
    }

    private static void EnsureProjectHasAdmin(ServerOptions options, string endpoint, IReadOnlyDictionary<string, PermissionTier>? overrides)
    {
        if (options.Users.Count == 0)
        {
            return;
        }

        var hasAdmin = options.Users.Any(kvp =>
        {
            var username = kvp.Key;
            var defaultTier = kvp.Value.DefaultTier;
            if (overrides is not null && overrides.TryGetValue(username, out var overrideTier))
            {
                return overrideTier == PermissionTier.Admin;
            }

            return defaultTier == PermissionTier.Admin;
        });

        if (!hasAdmin)
        {
            throw new InvalidOperationException($"Project '{endpoint}' must retain at least one Admin-tier user.");
        }
    }

    private async Task MutateAsync(Action<ServerOptions> mutation, CancellationToken cancellationToken)
    {
        ServerOptions current;
        lock (_sync)
        {
            current = CloneOptions(_optionsMonitor.CurrentValue);
            mutation(current);
        }

        await Persist(current, cancellationToken);
    }

    private static ServerOptions CloneOptions(ServerOptions source)
    {
        var serialized = JsonSerializer.Serialize(source, SerializerOptions);
        return JsonSerializer.Deserialize<ServerOptions>(serialized, SerializerOptions)
               ?? throw new InvalidOperationException("Failed to clone ServerOptions.");
    }

    private async Task Persist(ServerOptions options, CancellationToken cancellationToken)
    {
        var toml = BuildToml(options);
        Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
        await File.WriteAllTextAsync(_configPath, toml, Encoding.UTF8, cancellationToken);
        _configurationRoot.Reload();
        _logger.LogInformation("Persisted configuration changes to {Path}", _configPath);
    }

    private static string BuildToml(ServerOptions options)
    {
        var sb = new StringBuilder();
        AppendGlobal(sb, options.Global);
        AppendEndpoints(sb, options.Endpoints);
        AppendUsers(sb, options.Users);
        AppendPermissions(sb, options.Permissions);
        return sb.ToString();
    }

    private static void AppendGlobal(StringBuilder sb, GlobalOptions global)
    {
        sb.AppendLine("[global]");
        AppendString(sb, "serviceName", global.ServiceName);
        AppendString(sb, "httpUrl", global.HttpUrl);
        AppendString(sb, "embeddingModel", global.EmbeddingModel);
        AppendInt(sb, "embeddingDims", global.EmbeddingDims);
        AppendString(sb, "summaryModel", global.SummaryModel);
        AppendString(sb, "installLayoutManifest", global.InstallLayoutManifest);
        sb.AppendLine();
        sb.AppendLine("[global.backup]");
        AppendString(sb, "differentialCron", global.Backup.DifferentialCron);
        AppendString(sb, "fullCron", global.Backup.FullCron);
        AppendInt(sb, "retentionDays", global.Backup.RetentionDays);
        AppendInt(sb, "fullRetentionDays", global.Backup.FullRetentionDays);
        AppendString(sb, "targetPath", global.Backup.TargetPath ?? string.Empty);
        sb.AppendLine();

        AppendBackupUpload(sb, global.Backup.Upload);
    }

    private static void AppendBackupUpload(StringBuilder sb, BackupUploadOptions upload)
    {
        if (!upload.Enabled
            && string.IsNullOrWhiteSpace(upload.AccountUrl)
            && string.IsNullOrWhiteSpace(upload.Container)
            && string.IsNullOrWhiteSpace(upload.Prefix)
            && string.IsNullOrWhiteSpace(upload.SasTokenProtected)
            && string.IsNullOrWhiteSpace(upload.CertificateThumbprint))
        {
            return;
        }

        sb.AppendLine("[global.backup.upload]");
        if (upload.Enabled)
        {
            AppendBool(sb, "enabled", true);
        }
        AppendString(sb, "provider", upload.Provider);
        AppendString(sb, "accountUrl", upload.AccountUrl ?? string.Empty);
        AppendString(sb, "container", upload.Container ?? string.Empty);
        AppendString(sb, "prefix", upload.Prefix ?? string.Empty);
        AppendString(sb, "authMode", upload.AuthMode);
        AppendString(sb, "sasTokenProtected", upload.SasTokenProtected ?? string.Empty);
        AppendString(sb, "sasFingerprint", upload.SasFingerprint ?? string.Empty);
        AppendString(sb, "sasUpdatedUtc", upload.SasUpdatedUtc?.ToString("O") ?? string.Empty);
        AppendString(sb, "certificateThumbprint", upload.CertificateThumbprint ?? string.Empty);
        AppendString(sb, "certificateUpdatedUtc", upload.CertificateUpdatedUtc?.ToString("O") ?? string.Empty);
        sb.AppendLine();
    }

    private static void AppendEndpoints(StringBuilder sb, IReadOnlyDictionary<string, EndpointOptions> endpoints)
    {
        foreach (var (key, endpoint) in endpoints)
        {
            sb.AppendLine($"[endpoint.{key}]");
            AppendString(sb, "slug", endpoint.Slug);
            AppendString(sb, "name", endpoint.Name);
            AppendString(sb, "description", endpoint.Description);
            AppendString(sb, "storagePath", endpoint.StoragePath);
            AppendBool(sb, "includeInSearchByDefault", endpoint.IncludeInSearchByDefault);
            AppendBool(sb, "inheritShared", endpoint.InheritShared);
            sb.AppendLine();
        }
    }

    private static void AppendUsers(StringBuilder sb, IReadOnlyDictionary<string, UserOptions> users)
    {
        foreach (var (key, user) in users)
        {
            sb.AppendLine($"[users.{key}]");
            AppendString(sb, "apiKey", user.ApiKey);
            AppendString(sb, "defaultTier", user.DefaultTier.ToString());
            sb.AppendLine();
        }
    }

    private static void AppendPermissions(StringBuilder sb, IReadOnlyDictionary<string, Dictionary<string, PermissionTier>> permissions)
    {
        foreach (var (endpoint, assignments) in permissions)
        {
            if (assignments.Count == 0)
            {
                continue;
            }

            sb.AppendLine($"[permissions.{endpoint}]");
            foreach (var (user, tier) in assignments)
            {
                AppendString(sb, user, tier.ToString());
            }

            sb.AppendLine();
        }
    }

    private static void AppendString(StringBuilder sb, string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        sb.AppendLine($"{key} = \"{Escape(value)}\"");
    }

    private static void AppendBool(StringBuilder sb, string key, bool value)
    {
        sb.AppendLine($"{key} = {value.ToString().ToLowerInvariant()}");
    }

    private static void AppendInt(StringBuilder sb, string key, int value)
    {
        sb.AppendLine($"{key} = {value}");
    }

    private static string Escape(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}

public sealed record ConfigValidationResult(bool IsValid, IReadOnlyList<string> Errors);
