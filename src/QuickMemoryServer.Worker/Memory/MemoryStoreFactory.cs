using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuickMemoryServer.Worker.Configuration;
using QuickMemoryServer.Worker.Embeddings;
using QuickMemoryServer.Worker.Persistence;
using QuickMemoryServer.Worker.Search;
using QuickMemoryServer.Worker.Validation;

namespace QuickMemoryServer.Worker.Memory;

public sealed class MemoryStoreFactory : IMemoryStoreProvider
{
    private readonly object _sync = new();
    private readonly Dictionary<string, IMemoryStore> _stores = new(StringComparer.OrdinalIgnoreCase);
    private readonly IOptionsMonitor<ServerOptions> _optionsMonitor;
    private readonly JsonlRepository _repository;
    private readonly MemoryEntryValidator _validator;
    private readonly EmbeddingService _embeddingService;
    private readonly SearchEngine _searchEngine;
    private readonly ILoggerFactory _loggerFactory;

    public MemoryStoreFactory(
        IOptionsMonitor<ServerOptions> options,
        JsonlRepository repository,
        MemoryEntryValidator validator,
        EmbeddingService embeddingService,
        SearchEngine searchEngine,
        ILoggerFactory loggerFactory)
    {
        _optionsMonitor = options;
        _repository = repository;
        _validator = validator;
        _embeddingService = embeddingService;
        _searchEngine = searchEngine;
        _loggerFactory = loggerFactory;
    }

    public IReadOnlyDictionary<string, IMemoryStore> Stores
    {
        get
        {
            lock (_sync)
            {
                return new Dictionary<string, IMemoryStore>(_stores, StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    public IMemoryStore GetOrCreate(string endpointKey)
    {
        if (string.Equals(endpointKey, "shared", StringComparison.OrdinalIgnoreCase))
        {
            return EnsureSharedStore();
        }

        lock (_sync)
        {
            if (_stores.TryGetValue(endpointKey, out var cached))
            {
                return cached;
            }

            var optionsSnapshot = _optionsMonitor.CurrentValue;

            if (!optionsSnapshot.Endpoints.TryGetValue(endpointKey, out var endpointOptions))
            {
                throw new InvalidOperationException($"Endpoint '{endpointKey}' is not configured.");
            }

            var store = CreateStore(endpointKey, endpointOptions);

            _stores[endpointKey] = store;
            return store;
        }
    }

    public async Task InitializeAllAsync(CancellationToken cancellationToken)
    {
        var shared = EnsureSharedStore();
        await shared.InitializeAsync(cancellationToken);

        var options = _optionsMonitor.CurrentValue;

        foreach (var endpointKey in options.Endpoints.Keys)
        {
            var store = GetOrCreate(endpointKey);
            await store.InitializeAsync(cancellationToken);
        }
    }

    private IMemoryStore EnsureSharedStore()
    {
        lock (_sync)
        {
            if (_stores.TryGetValue("shared", out var cached))
            {
                return cached;
            }

            var options = _optionsMonitor.CurrentValue;

            var sharedOptions = options.Endpoints.TryGetValue("shared", out var configured)
                ? configured
                : new EndpointOptions
                {
                    Name = "Shared Memory",
                    StoragePath = Path.Combine(AppContext.BaseDirectory, "MemoryStores", "shared"),
                    IncludeInSearchByDefault = true,
                    InheritShared = false,
                    Slug = "shared",
                    Description = "Global shared memory"
                };

            var shared = CreateStore("shared", sharedOptions);

            _stores["shared"] = shared;
            return shared;
        }
    }

    private MemoryStore CreateStore(string endpointKey, EndpointOptions endpointOptions)
    {
        var snapshot = _optionsMonitor.CurrentValue;
        var name = string.IsNullOrWhiteSpace(endpointOptions.Name) ? endpointKey : endpointOptions.Name;
        var storagePath = string.IsNullOrWhiteSpace(endpointOptions.StoragePath)
            ? Path.Combine(AppContext.BaseDirectory, "MemoryStores", endpointKey)
            : endpointOptions.StoragePath;

        return new MemoryStore(
            name,
            endpointKey,
            storagePath,
            snapshot.Global.EmbeddingDims,
            _repository,
            _validator,
            _embeddingService,
            _searchEngine,
            _loggerFactory);
    }
}
