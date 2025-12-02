using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using QuickMemoryServer.Worker.Configuration;

namespace QuickMemoryServer.Worker.Services;

internal static class McpAuthorizationContext
{
    public const string ApiKeyItem = "QuickMemory.ApiKey";
    public const string EndpointItem = "QuickMemory.Endpoint";
    public const string TierItem = "QuickMemory.Tier";
    public const string UserItem = "QuickMemory.User";

    public static PermissionTier GetTier(RequestContext<CallToolRequestParams> context)
    {
        if (context.Items.TryGetValue(TierItem, out var value) && value is PermissionTier tier)
        {
            return tier;
        }

        return PermissionTier.Reader;
    }

    public static string? GetUser(RequestContext<CallToolRequestParams> context)
    {
        if (context.Items.TryGetValue(UserItem, out var value) && value is string user)
        {
            return user;
        }

        return null;
    }
}
