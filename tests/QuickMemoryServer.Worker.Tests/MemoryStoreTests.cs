using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using QuickMemoryServer.Worker.Embeddings;
using QuickMemoryServer.Worker.Memory;
using QuickMemoryServer.Worker.Models;
using QuickMemoryServer.Worker.Persistence;
using QuickMemoryServer.Worker.Search;
using QuickMemoryServer.Worker.Validation;

namespace QuickMemoryServer.Worker.Tests;

public sealed class MemoryStoreTests
{
    private readonly MemoryEntryValidator _validator = new();

    [Fact]
    public async Task InitializeAsync_LoadsEntriesFromDisk()
    {
        var dir = Directory.CreateTempSubdirectory();
        MemoryStore? store = null;
        EmbeddingService? embeddingService = null;
        SearchEngine? searchEngine = null;
        try
        {
            var path = Path.Combine(dir.FullName, "entries.jsonl");
            await File.WriteAllLinesAsync(path, new[]
            {
                "{\"id\":\"proj:1\",\"project\":\"proj\",\"kind\":\"note\",\"curationTier\":\"curated\",\"confidence\":0.9}"
            });

            var repository = new JsonlRepository(_validator, NullLogger<JsonlRepository>.Instance);
            var embeddingGenerator = new HashEmbeddingGenerator(3);
            embeddingService = new EmbeddingService(embeddingGenerator, NullLogger<EmbeddingService>.Instance);
            searchEngine = new SearchEngine(NullLoggerFactory.Instance);
            store = new MemoryStore("Project", "proj", dir.FullName, 0, repository, _validator, embeddingService, searchEngine, NullLoggerFactory.Instance);

            await store.InitializeAsync(CancellationToken.None);
            var snapshot = store.Snapshot();

            Assert.Single(snapshot);
            Assert.Equal("proj:1", snapshot.First().Id);
        }
        finally
        {
            searchEngine?.Dispose();
            embeddingService?.Dispose();
            store?.Dispose();
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task PersistAsync_WritesUpdatedEntries()
    {
        var dir = Directory.CreateTempSubdirectory();
        MemoryStore? store = null;
        EmbeddingService? embeddingService = null;
        SearchEngine? searchEngine = null;
        try
        {
            var repository = new JsonlRepository(_validator, NullLogger<JsonlRepository>.Instance);
            var embeddingGenerator = new HashEmbeddingGenerator(3);
            embeddingService = new EmbeddingService(embeddingGenerator, NullLogger<EmbeddingService>.Instance);
            searchEngine = new SearchEngine(NullLoggerFactory.Instance);
            store = new MemoryStore("Project", "proj", dir.FullName, 3, repository, _validator, embeddingService, searchEngine, NullLoggerFactory.Instance);
            await store.InitializeAsync(CancellationToken.None);

            var entry = new MemoryEntry
            {
                Id = "proj:2",
                Project = "proj",
                Kind = "note",
                Embedding = new double[] { 0.1, 0.2, 0.3 }
            };

            await store.AppendAsync(entry, CancellationToken.None);

            var path = Path.Combine(dir.FullName, "entries.jsonl");
            var lines = await File.ReadAllLinesAsync(path);
            Assert.Single(lines);
            Assert.Contains("\"id\":\"proj:2\"", lines[0]);
        }
        finally
        {
            searchEngine?.Dispose();
            embeddingService?.Dispose();
            store?.Dispose();
            dir.Delete(recursive: true);
        }
    }
}
