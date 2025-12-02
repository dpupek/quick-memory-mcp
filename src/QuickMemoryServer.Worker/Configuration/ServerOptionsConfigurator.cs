using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace QuickMemoryServer.Worker.Configuration;

/// <summary>
/// Custom binder that maps hierarchical configuration (TOML/INI/JSON) to <see cref="ServerOptions"/>.
/// Handles dictionaries like [endpoint.*] and [users.*] from the config file.
/// </summary>
public sealed class ServerOptionsConfigurator : IConfigureOptions<ServerOptions>
{
    private readonly IConfiguration _configuration;

    public ServerOptionsConfigurator(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public void Configure(ServerOptions options)
    {
        BindGlobal(options);
        BindEndpoints(options);
        BindUsers(options);
        BindPermissions(options);
    }

    private void BindGlobal(ServerOptions options)
    {
        var globalSection = _configuration.GetSection("global");
        if (globalSection.Exists())
        {
            globalSection.Bind(options.Global);
            globalSection.GetSection("backup").Bind(options.Global.Backup);
        }

        // Allow alternate casing (Global) for JSON appsettings.
        var globalAlternate = _configuration.GetSection("Global");
        if (globalAlternate.Exists())
        {
            globalAlternate.Bind(options.Global);
            globalAlternate.GetSection("Backup").Bind(options.Global.Backup);
        }
    }

    private void BindEndpoints(ServerOptions options)
    {
        options.Endpoints.Clear();

        foreach (var sectionName in new[] { "endpoint", "Endpoint", "endpoints", "Endpoints" })
        {
            var section = _configuration.GetSection(sectionName);
            if (!section.Exists())
            {
                continue;
            }

            foreach (var child in section.GetChildren())
            {
                var endpoint = new EndpointOptions();
                child.Bind(endpoint);
                options.Endpoints[child.Key] = endpoint;
            }
        }
    }

    private void BindUsers(ServerOptions options)
    {
        options.Users.Clear();

        foreach (var sectionName in new[] { "users", "Users" })
        {
            var section = _configuration.GetSection(sectionName);
            if (!section.Exists())
            {
                continue;
            }

            foreach (var child in section.GetChildren())
            {
                var user = new UserOptions();
                child.Bind(user);
                options.Users[child.Key] = user;
            }
        }
    }

    private void BindPermissions(ServerOptions options)
    {
        options.Permissions.Clear();

        foreach (var sectionName in new[] { "permissions", "Permissions" })
        {
            var section = _configuration.GetSection(sectionName);
            if (!section.Exists())
            {
                continue;
            }

            foreach (var endpointSection in section.GetChildren())
            {
                var map = new Dictionary<string, PermissionTier>(StringComparer.OrdinalIgnoreCase);
                foreach (var assignment in endpointSection.GetChildren())
                {
                    if (Enum.TryParse<PermissionTier>(assignment.Value, ignoreCase: true, out var tier))
                    {
                        map[assignment.Key] = tier;
                    }
                }

                options.Permissions[endpointSection.Key] = map;
            }
        }
    }
}
