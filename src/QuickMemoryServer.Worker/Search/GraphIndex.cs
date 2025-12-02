using QuickMemoryServer.Worker.Models;

namespace QuickMemoryServer.Worker.Search;

public sealed class GraphIndex
{
    private readonly Dictionary<string, HashSet<string>> _edges = new(StringComparer.OrdinalIgnoreCase);

    public void Rebuild(IEnumerable<MemoryEntry> entries)
    {
        _edges.Clear();
        foreach (var entry in entries)
        {
            if (!_edges.TryGetValue(entry.Id, out var neighbors))
            {
                neighbors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _edges[entry.Id] = neighbors;
            }

            foreach (var relation in entry.Relations ?? Array.Empty<MemoryRelation>())
            {
                if (string.IsNullOrWhiteSpace(relation.TargetId))
                {
                    continue;
                }

                neighbors.Add(relation.TargetId);

                if (!_edges.TryGetValue(relation.TargetId, out var backNeighbors))
                {
                    backNeighbors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    _edges[relation.TargetId] = backNeighbors;
                }

                backNeighbors.Add(entry.Id);
            }
        }
    }

    public IEnumerable<string> Related(string id, int maxHops)
    {
        if (!_edges.ContainsKey(id) || maxHops <= 0)
        {
            return Array.Empty<string>();
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { id };
        var queue = new Queue<(string node, int depth)>();
        queue.Enqueue((id, 0));
        var results = new List<string>();

        while (queue.Count > 0)
        {
            var (node, depth) = queue.Dequeue();
            if (!_edges.TryGetValue(node, out var neighbors))
            {
                continue;
            }

            foreach (var neighbor in neighbors)
            {
                if (!visited.Add(neighbor))
                {
                    continue;
                }

                results.Add(neighbor);

                if (depth + 1 < maxHops)
                {
                    queue.Enqueue((neighbor, depth + 1));
                }
            }
        }

        return results;
    }
}
