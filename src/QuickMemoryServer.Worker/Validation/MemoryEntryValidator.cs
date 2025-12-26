using System.Linq;
using QuickMemoryServer.Worker.Models;

namespace QuickMemoryServer.Worker.Validation;

public sealed class MemoryEntryValidator
{
    private static readonly HashSet<string> AllowedCurationTiers = new(StringComparer.OrdinalIgnoreCase)
    {
        "canonical",
        "curated",
        "provisional"
    };

    private static readonly HashSet<string> CanonicalBodyTypeHints = new(StringComparer.OrdinalIgnoreCase)
    {
        "text",
        "json",
        "markdown",
        "html",
        "xml",
        "yaml",
        "toml",
        "csv"
    };

    public MemoryEntry Normalize(MemoryEntry entry, int embeddingDimensions)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.SchemaVersion <= 0)
        {
            throw new MemoryValidationException("schemaVersion must be positive.");
        }

        if (string.IsNullOrWhiteSpace(entry.Id))
        {
            throw new MemoryValidationException("id is required.");
        }

        if (string.IsNullOrWhiteSpace(entry.Kind))
        {
            throw new MemoryValidationException("kind is required.");
        }

        var tags = entry.Tags?.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                   ?? Array.Empty<string>();

        var keywords = entry.Keywords?.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                       ?? Array.Empty<string>();

        var relations = entry.Relations?.Where(r => !string.IsNullOrWhiteSpace(r?.TargetId)).Select(r => new MemoryRelation
        {
            Type = string.IsNullOrWhiteSpace(r.Type) ? "ref" : r.Type,
            TargetId = r.TargetId.Trim(),
            Weight = r.Weight
        }).ToArray() ?? Array.Empty<MemoryRelation>();

        var timestamps = entry.Timestamps ?? new MemoryTimestamps();
        var now = DateTimeOffset.UtcNow;

        if (timestamps.CreatedUtc == DateTimeOffset.UnixEpoch || timestamps.CreatedUtc == default)
        {
            timestamps = timestamps with { CreatedUtc = now };
        }

        if (timestamps.UpdatedUtc == DateTimeOffset.UnixEpoch || timestamps.UpdatedUtc == default)
        {
            timestamps = timestamps with { UpdatedUtc = timestamps.CreatedUtc };
        }

        var embedding = entry.Embedding?.ToArray();
        if (embedding is { Length: > 0 })
        {
            if (embeddingDimensions > 0 && embedding.Length != embeddingDimensions)
            {
                throw new MemoryValidationException($"embedding length {embedding.Length} does not match configured dimension {embeddingDimensions}.");
            }
        }

        var confidence = entry.Confidence;
        if (confidence is < 0 or > 1)
        {
            throw new MemoryValidationException("confidence must be between 0 and 1.");
        }

        var tier = entry.CurationTier;
        if (string.IsNullOrWhiteSpace(tier))
        {
            tier = "provisional";
        }
        else if (!AllowedCurationTiers.Contains(tier))
        {
            throw new MemoryValidationException($"curationTier '{tier}' is not supported.");
        }

        var source = entry.Source ?? new MemorySource();
        var bodyTypeHint = string.IsNullOrWhiteSpace(entry.BodyTypeHint) ? null : entry.BodyTypeHint.Trim();
        if (!string.IsNullOrWhiteSpace(bodyTypeHint) && CanonicalBodyTypeHints.Contains(bodyTypeHint))
        {
            bodyTypeHint = bodyTypeHint.ToLowerInvariant();
        }

        return entry with
        {
            Tags = tags,
            Keywords = keywords,
            Relations = relations,
            Timestamps = timestamps,
            Embedding = embedding,
            Confidence = confidence,
            CurationTier = tier.ToLowerInvariant(),
            Title = string.IsNullOrWhiteSpace(entry.Title) ? null : entry.Title.Trim(),
            Source = source,
            BodyTypeHint = bodyTypeHint
        };
    }
}
