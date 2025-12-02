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
            return false;
        }

        var options = _optionsMonitor.CurrentValue;

        foreach (var (userId, userOptions) in options.Users)
        {
            if (!string.Equals(userOptions.ApiKey, apiKey, StringComparison.Ordinal))
            {
                continue;
            }

            userKey = userId;
            tier = userOptions.DefaultTier;

            if (options.Permissions.TryGetValue(endpointKey, out var endpointPermissions) &&
                endpointPermissions.TryGetValue(userId, out var overrideTier))
            {
                tier = overrideTier;
            }

            return true;
        }

        _logger.LogWarning("API key authentication failed for endpoint {Endpoint}", endpointKey);
        return false;
    }
}
