using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuickMemoryServer.Worker.Configuration;

namespace QuickMemoryServer.Worker.Services;

public sealed class ApiKeyAuthorizer
{
    private readonly IOptionsMonitor<ServerOptions> _optionsMonitor;
    private readonly ILogger<ApiKeyAuthorizer> _logger;

    public ApiKeyAuthorizer(IOptionsMonitor<ServerOptions> optionsMonitor, ILogger<ApiKeyAuthorizer> logger)
    {
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }

    public bool TryAuthorize(string apiKey, string endpointKey, out string userKey, out PermissionTier tier)
    {
        userKey = string.Empty;
        tier = PermissionTier.Reader;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("API key missing for endpoint {Endpoint}", endpointKey);
            return false;
        }

        var fingerprint = CreateFingerprint(apiKey);
        var options = _optionsMonitor.CurrentValue;

        foreach (var (userId, userOptions) in options.Users)
        {
            if (!string.Equals(userOptions.ApiKey, apiKey, StringComparison.Ordinal))
            {
                continue;
            }

            userKey = userId;
            tier = userOptions.DefaultTier;
            _logger.LogDebug("API key {ApiKeyFingerprint} matched user {User} with default tier {Tier}", fingerprint, userId, tier);

            if (options.Permissions.TryGetValue(endpointKey, out var endpointPermissions) &&
                endpointPermissions.TryGetValue(userId, out var overrideTier))
            {
                tier = overrideTier;
                _logger.LogDebug("Applying endpoint override tier {Tier} for user {User} on {Endpoint}", overrideTier, userId, endpointKey);
            }
            else
            {
                _logger.LogDebug("Using default tier {Tier} for user {User} on {Endpoint}", tier, userId, endpointKey);
            }

            return true;
        }

        _logger.LogWarning("API key authentication failed for endpoint {Endpoint} (fingerprint {ApiKeyFingerprint})", endpointKey, fingerprint);
        return false;
    }

    private static string CreateFingerprint(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return "empty";
        }

        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(apiKey));
        return Convert.ToHexString(hash.AsSpan(0, 8));
    }
}
