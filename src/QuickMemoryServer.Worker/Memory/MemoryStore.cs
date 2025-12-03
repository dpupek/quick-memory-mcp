using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using QuickMemoryServer.Worker.Diagnostics;
using QuickMemoryServer.Worker.Embeddings;
using QuickMemoryServer.Worker.Models;
using QuickMemoryServer.Worker.Persistence;
using QuickMemoryServer.Worker.Search;
using QuickMemoryServer.Worker.Validation;

namespace QuickMemoryServer.Worker.Memory;

public class MemoryStore : IMemoryStore, IDisposable
{
    private readonly ILogger<MemoryStore> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly EmbeddingService _embeddingService;
    private readonly SearchEngine _searchEngine;
    private readonly GraphIndex _graphIndex = new();
    private readonly JsonlRepository _repository;
    private readonly MemoryEntryValidator _validator;
    private readonly string _storagePath;
    private readonly int _embeddingDimensions;
    private ImmutableArray<MemoryEntry> _entries = ImmutableArray<MemoryEntry>.Empty;
    private readonly object _sync = new();
    private DebouncedFileWatcher? _watcher;
    private bool _dirty;

    public MemoryStore(
        string name,
        string project,
        string storagePath,
        int embeddingDimensions,
        JsonlRepository repository,
        MemoryEntryValidator validator,
        EmbeddingService embeddingService,
        SearchEngine searchEngine,
        ILoggerFactory loggerFactory)
    {
        Name = name;
        Project = project;
        _storagePath = storagePath;
        _repository = repository;
        _validator = validator;
        _loggerFactory = loggerFactory;
        _embeddingService = embeddingService;
        _searchEngine = searchEngine;
        _logger = loggerFactory.CreateLogger<MemoryStore>();
        _embeddingDimensions = embeddingDimensions;
    }

    public string Name { get; }

    public string Project { get; }

    public string StoragePath => _storagePath;

    public FileInfo? EntriesFileInfo => File.Exists(EntryFilePath) ? new FileInfo(EntryFilePath) : null;

    private string EntryFilePath => Path.Combine(_storagePath, "entries.jsonl");

    public async ValueTask InitializeAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(EntryFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var entries = await _repository.LoadAsync(EntryFilePath, _embeddingDimensions, cancellationToken);
        var filtered = entries.Where(e => string.Equals(e.Project, Project, StringComparison.OrdinalIgnoreCase)).ToList();

        if (filtered.Count != entries.Count)
        {
            _logger.LogWarning("Store {Store} skipping {Skipped} entries that belong to other projects.", Name, entries.Count - filtered.Count);
        }

        var enriched = await EnsureEmbeddingsAsync(filtered, cancellationToken);
        lock (_sync)
        {
            _entries = enriched.ToImmutableArray();
            _dirty = false;
        }

        RebuildIndexes();

        // start watcher
        _watcher = new DebouncedFileWatcher(
            Path.GetDirectoryName(EntryFilePath)!,
            Path.GetFileName(EntryFilePath),
            TimeSpan.FromMilliseconds(250),
            _loggerFactory,
            cancellationToken,
            () => ReloadFromDisk());

        _logger.LogInformation("Initialized memory store {Store} with {Count} entries.", Name, _entries.Length);
    }

    public IReadOnlyCollection<MemoryEntry> Snapshot()
    {
        lock (_sync)
        {
            return _entries;
        }
    }

    public MemoryEntry? FindEntry(string id)
    {
        lock (_sync)
        {
            return _entries.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
        }
    }

    public async ValueTask AppendAsync(MemoryEntry entry, CancellationToken cancellationToken)
    {
        var normalized = _validator.Normalize(entry, _embeddingDimensions);
        var enriched = await _embeddingService.EnsureEmbeddingAsync(normalized, cancellationToken);

        lock (_sync)
        {
            _entries = _entries.Add(enriched);
            _dirty = true;
        }

        await SaveInternalAsync(cancellationToken);
    }

    public async ValueTask UpsertAsync(MemoryEntry entry, CancellationToken cancellationToken)
    {
        var normalized = _validator.Normalize(entry, _embeddingDimensions);
        var enriched = await _embeddingService.EnsureEmbeddingAsync(normalized, cancellationToken);

        lock (_sync)
        {
            var index = IndexOf(enriched.Id);
            if (index >= 0)
            {
                _entries = _entries.SetItem(index, enriched);
            }
            else
            {
                _entries = _entries.Add(enriched);
            }
            _dirty = true;
        }

        await SaveInternalAsync(cancellationToken);
    }

    public async ValueTask<bool> DeleteAsync(string id, bool force, CancellationToken cancellationToken)
    {
        var removed = false;
        lock (_sync)
        {
            var index = IndexOf(id);
            if (index < 0)
            {
                return false;
            }

            if (_entries[index].IsPermanent && !force)
            {
                throw new InvalidOperationException($"Entry '{id}' is marked permanent.");
            }

            _entries = _entries.RemoveAt(index);
            _dirty = true;
            removed = true;
        }

        if (removed)
        {
            await SaveInternalAsync(cancellationToken);
        }

        return removed;
    }

    private int IndexOf(string id)
    {
        for (var i = 0; i < _entries.Length; i++)
        {
            if (string.Equals(_entries[i].Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    public async ValueTask PersistAsync(CancellationToken cancellationToken)
    {
        bool needsSave;
        lock (_sync)
        {
            needsSave = _dirty;
        }

        if (!needsSave)
        {
            return;
        }

        await SaveInternalAsync(cancellationToken);
    }

    private async Task SaveInternalAsync(CancellationToken cancellationToken)
    {
        ImmutableArray<MemoryEntry> snapshot;
        lock (_sync)
        {
            snapshot = _entries;
            _dirty = false;
        }

        await _repository.SaveAsync(EntryFilePath, snapshot, _embeddingDimensions, cancellationToken);
        RebuildIndexes(snapshot);
        _logger.LogInformation("Persisted {Count} entries for store {Store}.", snapshot.Length, Name);
    }

    private void ReloadFromDisk()
    {
        try
        {
            _logger.LogInformation("Detected change for {Store}, reloading from disk.", Name);
            var entries = _repository.LoadAsync(EntryFilePath, _embeddingDimensions, CancellationToken.None).GetAwaiter().GetResult();
            var enriched = EnsureEmbeddingsAsync(entries, CancellationToken.None).GetAwaiter().GetResult();
            lock (_sync)
            {
                _entries = enriched.ToImmutableArray();
                _dirty = false;
            }

            RebuildIndexes();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reload store {Store} from disk.", Name);
        }
    }

    private void RebuildIndexes()
    {
        ImmutableArray<MemoryEntry> snapshot;
        lock (_sync)
        {
            snapshot = _entries;
        }

        RebuildIndexes(snapshot);
    }

    private void RebuildIndexes(IEnumerable<MemoryEntry> entries)
    {
        var indexPath = Path.Combine(_storagePath, "indexes", "lucene");
        _searchEngine.Rebuild(Project, indexPath, entries);
        _graphIndex.Rebuild(entries);
    }

    private async Task<List<MemoryEntry>> EnsureEmbeddingsAsync(IEnumerable<MemoryEntry> entries, CancellationToken cancellationToken)
    {
        var list = new List<MemoryEntry>();
        foreach (var entry in entries)
        {
            var normalized = _validator.Normalize(entry, _embeddingDimensions);
            var enriched = await _embeddingService.EnsureEmbeddingAsync(normalized, cancellationToken);
            list.Add(enriched);
        }

        return list;
    }

    public IEnumerable<string> Related(string id, int maxHops)
    {
        return _graphIndex.Related(id, maxHops);
    }

    public void Dispose()
    {
        _watcher?.Dispose();
    }
}
