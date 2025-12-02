using QuickMemoryServer.Worker.Models;

namespace QuickMemoryServer.Worker.Search;

internal sealed class VectorIndex
{
    private readonly Dictionary<string, double[]> _vectors = new();

    public void Rebuild(IEnumerable<MemoryEntry> entries)
    {
        _vectors.Clear();
        foreach (var entry in entries)
        {
            if (entry.Embedding is { Count: > 0 })
            {
                _vectors[entry.Id] = entry.Embedding.Select(v => (double)v).ToArray();
            }
        }
    }

    public IEnumerable<(string id, double score)> Search(IReadOnlyList<double> query, int maxResults)
    {
        if (_vectors.Count == 0 || query is not { Count: > 0 })
        {
            return Array.Empty<(string, double)>();
        }

        var queryNorm = Math.Sqrt(query.Sum(v => v * v));
        if (queryNorm == 0)
        {
            return Array.Empty<(string, double)>();
        }

        var results = new List<(string id, double score)>();
        foreach (var (id, vector) in _vectors)
        {
            var score = CosineSimilarity(vector, query, queryNorm);
            results.Add((id, score));
        }

        return results
            .OrderByDescending(r => r.score)
            .Take(maxResults)
            .ToArray();
    }

    private static double CosineSimilarity(double[] vector, IReadOnlyList<double> query, double queryNorm)
    {
        var dot = 0d;
        for (var i = 0; i < Math.Min(vector.Length, query.Count); i++)
        {
            dot += vector[i] * query[i];
        }

        var vectorNorm = Math.Sqrt(vector.Sum(v => v * v));
        if (vectorNorm == 0)
        {
            return 0;
        }

        return dot / (vectorNorm * queryNorm);
    }
}
