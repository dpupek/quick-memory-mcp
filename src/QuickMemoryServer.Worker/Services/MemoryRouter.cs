using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuickMemoryServer.Worker.Configuration;
using QuickMemoryServer.Worker.Memory;

namespace QuickMemoryServer.Worker.Services;

public sealed class MemoryRouter
{
    private readonly MemoryStoreFactory _factory;
    private readonly IOptionsMonitor<ServerOptions> _optionsMonitor;
    private readonly ILogger<MemoryRouter> _logger;

    public MemoryRouter(MemoryStoreFactory factory, IOptionsMonitor<ServerOptions> options, ILogger<MemoryRouter> logger)
    {
        _factory = factory;
        _optionsMonitor = options;
        _logger = logger;
    }

    public IMemoryStore ResolveStore(string endpointKey)
    {
        if (TryResolveStore(endpointKey, out var store))
        {
            return store;
        }

        throw new KeyNotFoundException($"Endpoint '{endpointKey}' is not defined.");
    }

    public bool TryResolveStore(string endpointKey, out MemoryStore store)
    {
        store = null!;

        if (string.IsNullOrWhiteSpace(endpointKey))
        {
            return false;
        }

        var options = _optionsMonitor.CurrentValue;

        if (options.Endpoints.ContainsKey(endpointKey) ||
            string.Equals(endpointKey, "shared", StringComparison.OrdinalIgnoreCase))
        {
            store = (MemoryStore)_factory.GetOrCreate(endpointKey);
            return true;
        }

        _logger.LogWarning("Attempted to resolve unknown endpoint {Endpoint}", endpointKey);
        return false;
    }
}
