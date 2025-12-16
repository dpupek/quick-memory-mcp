using System.Text.Json.Nodes;

namespace QuickMemoryServer.Worker.Models;

public sealed record MemoryEntry
{
    public int SchemaVersion { get; init; } = 1;

    public string Id { get; init; } = string.Empty;

    public string Project { get; init; } = string.Empty;

    public string Kind { get; init; } = "note";

    public string? Title { get; init; }
        = null;

    public JsonNode? Body { get; init; }
        = null;

    public string? BodyTypeHint { get; init; }
        = null;

    public IReadOnlyList<string> Tags { get; init; }
        = Array.Empty<string>();

    public MemorySource Source { get; init; } = new();

    public IReadOnlyList<double>? Embedding { get; init; }
        = null;

    public IReadOnlyList<string> Keywords { get; init; }
        = Array.Empty<string>();

    public IReadOnlyList<MemoryRelation> Relations { get; init; }
        = Array.Empty<MemoryRelation>();

    public MemoryTimestamps Timestamps { get; init; } = new();

    public DateTimeOffset? TtlUtc { get; init; }
        = null;

    public double Confidence { get; init; } = 0.5d;

    public string CurationTier { get; init; } = "provisional";

    public string? EpicSlug { get; init; }
        = null;

    public string? EpicCase { get; init; }
        = null;

    public bool IsPermanent { get; init; }
        = false;

    public bool Pinned { get; init; }
        = false;
}

public sealed record MemorySource
{
    public string? Type { get; init; }
        = null;

    public string? Path { get; init; }
        = null;

    public string? Url { get; init; }
        = null;

    public string? Shard { get; init; }
        = null;
}

public sealed record MemoryRelation
{
    public string Type { get; init; } = "ref";

    public string TargetId { get; init; } = string.Empty;

    public double? Weight { get; init; }
        = null;
}

public sealed record MemoryTimestamps
{
    public DateTimeOffset CreatedUtc { get; init; }
        = DateTimeOffset.UnixEpoch;

    public DateTimeOffset UpdatedUtc { get; init; }
        = DateTimeOffset.UnixEpoch;

    public DateTimeOffset? SourceUtc { get; init; }
        = null;
}
