using Microsoft.Extensions.Logging.Abstractions;
using QuickMemoryServer.Worker.Models;
using QuickMemoryServer.Worker.Persistence;
using QuickMemoryServer.Worker.Validation;

namespace QuickMemoryServer.Worker.Tests;

public sealed class JsonlRepositoryTests
{
    private readonly JsonlRepository _repository = new(new MemoryEntryValidator(), NullLogger<JsonlRepository>.Instance);

    [Fact]
    public async Task SaveAndLoad_RoundTripsEntries()
    {
#region Arrange
        var tempDirectory = Directory.CreateTempSubdirectory();
        var path = Path.Combine(tempDirectory.FullName, "entries.jsonl");

        var entries = new[]
        {
            new MemoryEntry
            {
                Id = "projectA:1",
                Kind = "fact",
                Body = null,
                BodyTypeHint = " YAML ",
                Tags = new[] { "search", "indexing" },
                Embedding = new double[] { 0.1, 0.2, 0.3 },
                CurationTier = "Canonical",
                Timestamps = new MemoryTimestamps
                {
                    CreatedUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
                    UpdatedUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
                }
            },
            new MemoryEntry
            {
                Id = "shared:2",
                Kind = "note",
                Title = "  Sample  ",
                Embedding = new double[] { 0.4, 0.5, 0.6 }
            }
        };

#endregion

#region Assert (initial state)
        Assert.Equal(" YAML ", entries[0].BodyTypeHint);
#endregion

#region Act
        await _repository.SaveAsync(path, entries, embeddingDimensions: 3, CancellationToken.None);
        var loaded = await _repository.LoadAsync(path, embeddingDimensions: 3, CancellationToken.None);
#endregion

#region Assert (post state)
        Assert.Equal(2, loaded.Count);
        var rawLines = await File.ReadAllLinesAsync(path);
        Assert.All(rawLines, line => Assert.DoesNotContain("\"project\"", line));
        Assert.Equal("projectA:1", loaded[0].Id);
        Assert.Equal("canonical", loaded[0].CurationTier);
        Assert.Equal("yaml", loaded[0].BodyTypeHint);
        Assert.Equal(3, loaded[0].Embedding!.Count);
        Assert.True(loaded[0].Timestamps.CreatedUtc > DateTimeOffset.UnixEpoch);
        Assert.Equal("shared:2", loaded[1].Id);
        Assert.Equal("Sample", loaded[1].Title);
#endregion
    }

    [Fact]
    public async Task LoadAsync_CreatesFileWhenMissing()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();
        var path = Path.Combine(tempDirectory.FullName, "missing.jsonl");

        var loaded = await _repository.LoadAsync(path, embeddingDimensions: 3, CancellationToken.None);

        Assert.Empty(loaded);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task SaveAsync_ThrowsWhenEmbeddingDimensionsMismatch()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();
        var path = Path.Combine(tempDirectory.FullName, "entries.jsonl");

        var entry = new MemoryEntry
        {
            Id = "projectA:bad",
            Kind = "fact",
            Embedding = new double[] { 0.1, 0.2 }
        };

        await Assert.ThrowsAsync<MemoryValidationException>(() =>
            _repository.SaveAsync(path, new[] { entry }, embeddingDimensions: 3, CancellationToken.None));
    }
}
