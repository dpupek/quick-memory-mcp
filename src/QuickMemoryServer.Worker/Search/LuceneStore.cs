using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Microsoft.Extensions.Logging;
using QuickMemoryServer.Worker.Models;

namespace QuickMemoryServer.Worker.Search;

internal sealed class LuceneStore : IDisposable
{
    private static readonly LuceneVersion Version = LuceneVersion.LUCENE_48;
    private readonly Lucene.Net.Store.Directory _directory;
    private readonly Analyzer _analyzer;
    private readonly ILogger<LuceneStore> _logger;

    public LuceneStore(string indexPath, ILogger<LuceneStore> logger)
    {
        System.IO.Directory.CreateDirectory(indexPath);
        _directory = FSDirectory.Open(indexPath);
        _analyzer = new StandardAnalyzer(Version);
        _logger = logger;
    }

    public void Rebuild(IEnumerable<MemoryEntry> entries)
    {
        var config = new IndexWriterConfig(Version, _analyzer)
        {
            OpenMode = OpenMode.CREATE
        };

        using var writer = new IndexWriter(_directory, config);
        foreach (var entry in entries)
        {
            var doc = new Document
            {
                new StringField("id", entry.Id, Field.Store.YES),
                new StringField("project", entry.Project, Field.Store.YES),
                new StringField("kind", entry.Kind, Field.Store.YES),
                new TextField("title", entry.Title ?? string.Empty, Field.Store.YES),
                new TextField("body", entry.Body?.ToJsonString() ?? string.Empty, Field.Store.NO),
                new TextField("tags", string.Join(' ', entry.Tags ?? Array.Empty<string>()), Field.Store.NO)
            };
            writer.AddDocument(doc);
        }

        writer.Commit();
        _logger.LogInformation("Lucene index rebuilt with {Count} documents.", entries.Count());
    }

    public IEnumerable<(string id, string project, float score, string snippet)> Search(string? text, int maxResults)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<(string, string, float, string)>();
        }

        using var reader = DirectoryReader.Open(_directory);
        var searcher = new IndexSearcher(reader);
        var parser = new MultiFieldQueryParser(Version, new[] { "title", "body", "tags" }, _analyzer);
        var query = parser.Parse(QueryParserBase.Escape(text));
        var hits = searcher.Search(query, maxResults);

        return hits.ScoreDocs.Select(hit =>
        {
            var doc = searcher.Doc(hit.Doc);
            return (
                doc.Get("id"),
                doc.Get("project"),
                hit.Score,
                doc.Get("title")
            );
        }).ToArray();
    }

    public void Dispose()
    {
        _directory.Dispose();
        _analyzer.Dispose();
    }
}
