using System.Text.Json.Nodes;

namespace QuickMemoryServer.Worker.Models;

public sealed record SearchRequest
{
    public string? Text { get; init; }
    public double[]? Embedding { get; init; }
    public int? MaxResults { get; init; }
    public bool? IncludeShared { get; init; }
    public string[]? Tags { get; init; }
}

public sealed record RelatedRequest
{
    public string Id { get; init; } = string.Empty;
    public int? MaxHops { get; init; }
    public bool? IncludeShared { get; init; }
}

public sealed record EntryPatchRequest
{
    public string? Title { get; init; }
    public string[]? Tags { get; init; }
    public string? CurationTier { get; init; }
    public bool? IsPermanent { get; init; }
    public bool? Pinned { get; init; }
    public double? Confidence { get; init; }
    public JsonNode? Body { get; init; }
    public string? BodyTypeHint { get; init; }
    public string? EpicSlug { get; init; }
    public string? EpicCase { get; init; }
    public JsonNode? Relations { get; init; }
    public JsonNode? Source { get; init; }
}

public sealed record BackupRequestPayload
{
    public string? Mode { get; init; }
}
