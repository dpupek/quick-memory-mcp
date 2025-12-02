using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using QuickMemoryServer.Worker.Models;

namespace QuickMemoryServer.Worker.Embeddings;

public sealed class EmbeddingService : IDisposable
{
    private readonly IEmbeddingGenerator _generator;
    private readonly ILogger<EmbeddingService> _logger;

    public EmbeddingService(IEmbeddingGenerator generator, ILogger<EmbeddingService> logger)
    {
        _generator = generator;
        _logger = logger;
    }

    public int Dimension => _generator.Dimension;

    public async Task<MemoryEntry> EnsureEmbeddingAsync(MemoryEntry entry, CancellationToken cancellationToken)
    {
        if (entry.Embedding is { Count: > 0 })
        {
            return entry;
        }

        var text = (entry.Title ?? string.Empty) + "\n" + entry.Body?.ToJsonString();
        try
        {
            var vector = await _generator.GenerateAsync(text, cancellationToken);
            return entry with { Embedding = vector.ToArray() };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate embedding for entry {EntryId}", entry.Id);
            return entry;
        }
    }

    public void Dispose() => _generator.Dispose();
}
