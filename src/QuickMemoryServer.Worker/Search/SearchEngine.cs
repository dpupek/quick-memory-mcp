using Microsoft.Extensions.Logging;
using QuickMemoryServer.Worker.Models;

namespace QuickMemoryServer.Worker.Search;

public sealed class SearchEngine : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly Dictionary<string, LuceneStore> _luceneStores = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, VectorIndex> _vectorStores = new(StringComparer.OrdinalIgnoreCase);

    public SearchEngine(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public void Rebuild(string key, string indexPath, IEnumerable<MemoryEntry> entries)
    {
        var lucene = GetLucene(key, indexPath);
        lucene.Rebuild(entries);

        var vector = GetVector(key);
        vector.Rebuild(entries);
    }

    public IEnumerable<SearchResult> Search(SearchQuery query, Func<string, MemoryEntry?> entryResolver)
    {
        var results = new List<SearchResult>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (id, project, score, snippet) in LuceneResults(query))
        {
            if (!seen.Add(id))
            {
                continue;
            }

            var entry = entryResolver(id);
            if (entry is null)
            {
                continue;
            }

            results.Add(new SearchResult(id, project, score, entry.Title, entry.Kind, snippet));
        }

        if (query.Embedding is { Count: > 0 })
        {
            foreach (var vectorResult in VectorResults(query))
            {
                if (!seen.Add(vectorResult.id))
                {
                    continue;
                }

                var entry = entryResolver(vectorResult.id);
                if (entry is null)
                {
                    continue;
                }

                results.Add(new SearchResult(entry.Id, entry.Project, vectorResult.score, entry.Title, entry.Kind, entry.Title));
            }
        }

        return results
            .OrderByDescending(r => r.Score)
            .Take(query.MaxResults)
            .ToArray();
    }

    private IEnumerable<(string id, string project, float score, string snippet)> LuceneResults(SearchQuery query)
    {
        var keys = ProjectsForQuery(query);
        var results = new List<(string id, string project, float score, string snippet)>();

        foreach (var key in keys)
        {
            if (!_luceneStores.TryGetValue(key, out var store))
            {
                continue;
            }

            results.AddRange(store.Search(query.Text, query.MaxResults));
        }

        return results;
    }

    private IEnumerable<(string id, double score)> VectorResults(SearchQuery query)
    {
        var keys = ProjectsForQuery(query);
        var results = new List<(string id, double score)>();

        foreach (var key in keys)
        {
            if (!_vectorStores.TryGetValue(key, out var store))
            {
                continue;
            }

            results.AddRange(store.Search(query.Embedding!, query.MaxResults));
        }

        return results;
    }

    private IEnumerable<string> ProjectsForQuery(SearchQuery query)
    {
        if (!query.IncludeShared || string.Equals(query.Project, "shared", StringComparison.OrdinalIgnoreCase))
        {
            return new[] { query.Project };
        }

        return new[] { query.Project, "shared" };
    }

    private LuceneStore GetLucene(string key, string indexPath)
    {
        if (_luceneStores.TryGetValue(key, out var store))
        {
            return store;
        }

        var created = new LuceneStore(indexPath, _loggerFactory.CreateLogger<LuceneStore>());
        _luceneStores[key] = created;
        return created;
    }

    private VectorIndex GetVector(string key)
    {
        if (_vectorStores.TryGetValue(key, out var store))
        {
            return store;
        }

        store = new VectorIndex();
        _vectorStores[key] = store;
        return store;
    }

    public void Dispose()
    {
        foreach (var store in _luceneStores.Values)
        {
            store.Dispose();
        }
        _luceneStores.Clear();
        _vectorStores.Clear();
    }
}
