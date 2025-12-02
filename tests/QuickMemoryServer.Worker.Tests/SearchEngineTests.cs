using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using QuickMemoryServer.Worker.Models;
using QuickMemoryServer.Worker.Search;

namespace QuickMemoryServer.Worker.Tests;

public sealed class SearchEngineTests
{
    [Fact]
    public void Search_ReturnsKeywordMatches()
    {
        var engine = new SearchEngine(NullLoggerFactory.Instance);
        var entries = new[]
        {
            new MemoryEntry
            {
                Id = "proj:alpha",
                Project = "proj",
                Kind = "note",
                Title = "Index rebuild",
                Tags = new[] { "search" },
                Embedding = new double[] { 0.1, 0.2, 0.3 }
            },
            new MemoryEntry
            {
                Id = "proj:beta",
                Project = "proj",
                Kind = "note",
                Title = "Unrelated",
                Embedding = new double[] { 0.3, 0.2, 0.1 }
            }
        };

        var indexPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            engine.Rebuild("proj", indexPath, entries);

            var query = new SearchQuery
            {
                Project = "proj",
                Text = "rebuild",
                MaxResults = 5,
                IncludeShared = false
            };

            var results = engine.Search(query, id => entries.FirstOrDefault(e => e.Id == id)).ToArray();

            Assert.Single(results);
            Assert.Equal("proj:alpha", results[0].EntryId);
        }
        finally
        {
            engine.Dispose();
            if (Directory.Exists(indexPath))
            {
                Directory.Delete(indexPath, recursive: true);
            }
        }
    }
}
