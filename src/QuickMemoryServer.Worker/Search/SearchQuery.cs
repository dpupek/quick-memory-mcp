namespace QuickMemoryServer.Worker.Search;

public sealed record SearchQuery
{
    public string Project { get; init; } = string.Empty;

    public string? Text { get; init; }
        = null;

    public IReadOnlyList<double>? Embedding { get; init; }
        = null;

    public int MaxResults { get; init; } = 20;

    public bool IncludeShared { get; init; } = true;

    public IReadOnlyList<string>? Tags { get; init; }
        = null;
}

public sealed record SearchResult(string EntryId, string Project, double Score, string? Title, string Kind, string? Snippet);
