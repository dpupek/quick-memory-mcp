using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Microsoft.Extensions.Options;
using QuickMemoryServer.Worker.Configuration;
using QuickMemoryServer.Worker.Memory;
using QuickMemoryServer.Worker.Models;
using QuickMemoryServer.Worker.Search;
using QuickMemoryServer.Worker.Diagnostics;

namespace QuickMemoryServer.Worker.Services;

[McpServerToolType]
public static class MemoryMcpTools
{
    private static readonly JsonSerializerOptions JsonOptions = new();

[McpServerTool(Name = "searchEntries", Title = "Search memory entries", ReadOnly = true)]
[McpMeta("description", "Hybrid text + vector search across the endpoint (optionally including shared memory).")]
[McpMeta("tier", "reader")]
[McpMeta("recipe", "POST with { text, embedding, includeShared, tags, maxResults } (maxResults ≤ 200).")]
public static SearchEntriesResponse SearchEntries(
        string endpoint,
        SearchRequest? request,
        MemoryRouter router,
        SearchEngine searchEngine,
        IOptionsMonitor<ServerOptions> optionsMonitor)
    {
        request ??= new SearchRequest();
        var maxResults = request.MaxResults is > 0 and <= 200 ? request.MaxResults.Value : 20;
        var includeSharedDefault = optionsMonitor.CurrentValue.Endpoints.TryGetValue(endpoint, out var endpointOptions)
            ? endpointOptions.IncludeInSearchByDefault
            : true;
        var includeShared = request.IncludeShared ?? includeSharedDefault;

        if (router.ResolveStore(endpoint) is not MemoryStore primaryStore)
        {
            return new SearchEntriesResponse(Array.Empty<SearchEntryResult>());
        }

        var stores = new List<MemoryStore> { primaryStore };
        if (includeShared && !string.Equals(endpoint, "shared", StringComparison.OrdinalIgnoreCase))
        {
            if (router.ResolveStore("shared") is MemoryStore sharedStore)
            {
                stores.Add(sharedStore);
            }
        }

        var entryLookup = McpHelpers.BuildEntryLookup(stores);
        var query = new SearchQuery
        {
            Project = endpoint,
            Text = string.IsNullOrWhiteSpace(request.Text) ? null : request.Text,
            Embedding = request.Embedding,
            MaxResults = maxResults,
            IncludeShared = includeShared,
            Tags = request.Tags
        };

        var results = searchEngine
            .Search(query, id => entryLookup.TryGetValue(id, out var entry) ? entry : null)
            .ToArray();

        if (request.Tags is { Length: > 0 })
        {
            var tagSet = new HashSet<string>(request.Tags.Where(t => !string.IsNullOrWhiteSpace(t)), StringComparer.OrdinalIgnoreCase);
            results = results
                .Where(r => entryLookup.TryGetValue(r.EntryId, out var entry) && entry.Tags.Any(tag => tagSet.Contains(tag)))
                .ToArray();
        }

        var response = results
            .Select(r => entryLookup.TryGetValue(r.EntryId, out var entry)
                ? new SearchEntryResult(r.Score, r.Snippet, entry)
                : null)
            .Where(r => r is not null)
            .Cast<SearchEntryResult>()
            .ToList();

        return new SearchEntriesResponse(response);
    }

[McpServerTool(Name = "relatedEntries", Title = "Find related entries", ReadOnly = true)]
[McpMeta("description", "Walk the relation graph for an entry and return related node metadata.")]
[McpMeta("tier", "reader")]
[McpMeta("recipe", "Use maxHops=2+ and includeShared=true to see shared context.")]
public static object RelatedEntries(
        string endpoint,
        RelatedRequest request,
        MemoryRouter router)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Id))
        {
            return ErrorResult("missing-id");
        }

        var maxHops = request.MaxHops.GetValueOrDefault(2);
        if (maxHops < 1)
        {
            maxHops = 1;
        }

        if (router.ResolveStore(endpoint) is not MemoryStore primaryStore)
        {
            return ErrorResult($"Endpoint '{endpoint}' is not available.");
        }

        var includeShared = request.IncludeShared ?? true;
        var stores = new List<MemoryStore> { primaryStore };
        if (includeShared && !string.Equals(endpoint, "shared", StringComparison.OrdinalIgnoreCase))
        {
            if (router.ResolveStore("shared") is MemoryStore sharedStore)
            {
                stores.Add(sharedStore);
            }
        }

        var relatedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var store in stores)
        {
            foreach (var id in store.Related(request.Id, maxHops))
            {
                relatedIds.Add(id);
            }
        }

        var nodes = relatedIds
            .Select(id => McpHelpers.ResolveEntry(id, stores, router))
            .Where(entry => entry is not null)
            .Select(entry => new RelatedEntryNode(entry!.Id, entry.Project, entry.Title, entry.Kind))
            .ToList();

        var edges = relatedIds.Select(id => new RelatedEntryEdge(request.Id, id)).ToList();

        return new RelatedEntriesResponse(nodes, edges);
    }

[McpServerTool(Name = "getEntry", Title = "Get entry details", ReadOnly = true)]
[McpMeta("description", "Fetch a single entry, including body, tiers, and metadata.")]
[McpMeta("tier", "reader")]
public static object GetEntry(string endpoint, string id, MemoryRouter router)
    {
        if (router.ResolveStore(endpoint) is not MemoryStore store)
        {
            return ErrorResult($"Endpoint '{endpoint}' is not available.");
        }

        var entry = store.FindEntry(id);
        if (entry is null)
        {
            return ErrorResult("not-found");
        }

        return entry;
    }

[McpServerTool(Name = "listEntries", Title = "List all entries", ReadOnly = true)]
[McpMeta("description", "Return the full snapshot of entries for the project (includes shared data when included).")]
[McpMeta("tier", "reader")]
public static object ListEntries(string endpoint, MemoryRouter router)
    {
        if (router.ResolveStore(endpoint) is not MemoryStore store)
        {
            return ErrorResult($"Endpoint '{endpoint}' is not available.");
        }

        return store.Snapshot();
    }

[McpServerTool(Name = "listRecentEntries", Title = "List recent entries", ReadOnly = true)]
[McpMeta("description", "Browse the most recently updated entries without specifying a query.")]
[McpMeta("tier", "reader")]
public static object ListRecentEntries(string endpoint, int? maxResults, MemoryRouter router)
    {
        if (router.ResolveStore(endpoint) is not MemoryStore store)
        {
            return ErrorResult($"Endpoint '{endpoint}' is not available.");
        }

        var take = maxResults is > 0 and <= 200 ? maxResults.Value : 20;
        var recent = store.Snapshot()
            .OrderByDescending(e => e.Timestamps?.UpdatedUtc ?? e.Timestamps?.CreatedUtc ?? DateTimeOffset.MinValue)
            .ThenBy(e => e.Id)
            .Take(take)
            .Select(e => new
            {
                score = 1.0,
                snippet = e.Title ?? e.Id,
                entry = e
            })
            .ToArray();

        return new { results = recent };
    }

[McpServerTool(Name = "upsertEntry", Title = "Upsert entry")]
[McpMeta("description", "Insert/update entries, including tags, relations, epic context, and tier information.")]
[McpMeta("tier", "editor")]
[McpMeta("recipe", "Ensure curationTier/permanent flags match your tier before saving.")]
public static async Task<object> UpsertEntry(
        string endpoint,
        MemoryEntry entry,
        MemoryRouter router,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken)
    {
    if (entry is null)
    {
        return ErrorResult("invalid-entry");
    }

    if (!string.Equals(entry.Project, endpoint, StringComparison.OrdinalIgnoreCase))
    {
        return ErrorResult("project-mismatch: set entry.project to the endpoint you are calling");
    }

    var relationsError = ValidateRelations(entry.Relations);
    if (relationsError is not null)
    {
        return ErrorResult(relationsError);
    }

    var sourceError = ValidateSource(entry.Source);
    if (sourceError is not null)
    {
        return ErrorResult(sourceError);
    }

        var tier = McpAuthorizationContext.GetTier(context);
        if (entry.IsPermanent && tier != PermissionTier.Admin)
        {
            return ErrorResult("permanent entries require admin tier");
        }

        if (router.ResolveStore(endpoint) is not MemoryStore store)
        {
            return ErrorResult($"Endpoint '{endpoint}' is not available.");
        }

        await store.UpsertAsync(entry, cancellationToken);
        return new { updated = true };
    }

[McpServerTool(Name = "patchEntry", Title = "Patch entry")]
[McpMeta("description", "Update metadata fields (tags, tier, confidence, body, epic context) without full payload.")]
[McpMeta("tier", "editor")]
    public static async Task<object> PatchEntry(
        string endpoint,
        string id,
        EntryPatchRequest patch,
        MemoryRouter router,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken)
    {
    if (router.ResolveStore(endpoint) is not MemoryStore store)
    {
        return ErrorResult($"Endpoint '{endpoint}' is not available.");
    }

    var existing = store.FindEntry(id);
    if (existing is null)
    {
        return ErrorResult("not-found");
    }

    var relationsError = ValidateRelations(patch.Relations);
    if (relationsError is not null)
    {
        return ErrorResult(relationsError);
    }

    var sourceError = ValidateSource(patch.Source);
    if (sourceError is not null)
    {
        return ErrorResult(sourceError);
    }

    var updated = existing with
    {
        Title = patch.Title ?? existing.Title,
        Tags = patch.Tags ?? existing.Tags,
        CurationTier = patch.CurationTier ?? existing.CurationTier,
        IsPermanent = patch.IsPermanent ?? existing.IsPermanent,
        Pinned = patch.Pinned ?? existing.Pinned,
        Confidence = patch.Confidence ?? existing.Confidence,
        Body = patch.Body ?? existing.Body,
        EpicSlug = patch.EpicSlug ?? existing.EpicSlug,
        EpicCase = patch.EpicCase ?? existing.EpicCase,
        Relations = patch.Relations is null ? existing.Relations : patch.Relations.Deserialize<MemoryRelation[]>(JsonOptions),
        Source = patch.Source is null ? existing.Source : patch.Source.Deserialize<MemorySource>(JsonOptions)
    };

        var tier = McpAuthorizationContext.GetTier(context);
        if (updated.IsPermanent && tier != PermissionTier.Admin)
        {
            return ErrorResult("permanent entries require admin tier");
        }

        await store.UpsertAsync(updated, cancellationToken);
        return new { updated = true };
    }

[McpServerTool(Name = "deleteEntry", Title = "Delete entry")]
[McpMeta("description", "Delete non-permanent entries or force-remove them as admin.")]
[McpMeta("tier", "curator")]
[McpMeta("helpUrl", "/docs/agent-usage.md#curation-tier-matrix")]
public static async Task<object> DeleteEntry(
        string endpoint,
        string id,
        bool force,
        MemoryRouter router,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken)
    {
        if (router.ResolveStore(endpoint) is not MemoryStore store)
        {
            return ErrorResult($"Endpoint '{endpoint}' is not available.");
        }

        var tier = McpAuthorizationContext.GetTier(context);
        var permissionWhenForced = force || tier == PermissionTier.Admin;

        try
        {
            var deleted = await store.DeleteAsync(id, permissionWhenForced, cancellationToken);
            if (!deleted)
            {
                return ErrorResult("not-found");
            }

            return new { deleted = true };
        }
        catch (InvalidOperationException ex)
        {
            return ErrorResult(ex.Message);
        }
    }

[McpServerTool(Name = "requestBackup", Title = "Request backup")]
[McpMeta("description", "Trigger full or differential backups for compliance.")]
[McpMeta("tier", "admin")]
[McpMeta("recipe", "POST with { mode: 'differential'|'full' }.")]
public static async Task<object> RequestBackup(
        string endpoint,
        BackupRequestPayload payload,
        BackupService backupService,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken)
    {
        var tier = McpAuthorizationContext.GetTier(context);
        if (tier != PermissionTier.Admin)
        {
            return ErrorResult("backup operations require admin tier");
        }

        var mode = Enum.TryParse<BackupMode>(payload.Mode ?? "Differential", true, out var parsed)
            ? parsed
            : BackupMode.Differential;

        await backupService.RequestBackupAsync(endpoint, mode, cancellationToken);
        return new { queued = true, mode = mode.ToString() };
    }

[McpServerTool(Name = "listProjects", Title = "List endpoints", ReadOnly = true)]
[McpMeta("description", "Return metadata for configured endpoints and their storage settings.")]
[McpMeta("tier", "reader")]
public static ListProjectsResponse ListProjects(IOptionsMonitor<ServerOptions> optionsMonitor)
    {
        var endpoints = optionsMonitor.CurrentValue.Endpoints.Select(entry =>
            new EndpointSummary(
                entry.Key,
                entry.Value.Name,
                entry.Value.Slug,
                entry.Value.Description,
                entry.Value.StoragePath,
                entry.Value.InheritShared,
                entry.Value.IncludeInSearchByDefault))
            .ToList();

        return new ListProjectsResponse(endpoints);
    }

[McpServerTool(Name = "health", Title = "Health report", ReadOnly = true)]
[McpMeta("description", "Expose stores, uptime, and issue counts that the SPA surfaces.")]
[McpMeta("tier", "reader")]
public static HealthReport GetHealth(HealthReporter healthReporter)
    {
        return healthReporter.GetReport();
    }

    private static CallToolResult ErrorResult(string message)
    {
        return new CallToolResult
        {
            IsError = true,
            Content = new List<ContentBlock>
            {
                new TextContentBlock
                {
                    Text = message
                }
            }
        };
    }

    public sealed record SearchEntriesResponse(
        [property: JsonPropertyName("results")] IReadOnlyList<SearchEntryResult> Results);
    public sealed record SearchEntryResult(double Score, string? Snippet, MemoryEntry Entry);

    public sealed record RelatedEntriesResponse(
        [property: JsonPropertyName("nodes")] IReadOnlyList<RelatedEntryNode> Nodes,
        [property: JsonPropertyName("edges")] IReadOnlyList<RelatedEntryEdge> Edges);
    
    public sealed record RelatedEntryNode(string Id, string Project, string? Title, string Kind);

    public sealed record RelatedEntryEdge(string Source, string Target);

    public sealed record ListProjectsResponse(
        [property: JsonPropertyName("endpoints")] IReadOnlyList<EndpointSummary> Endpoints);
    
    public sealed record EndpointSummary(
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("slug")] string Slug,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("storagePath")] string StoragePath,
        [property: JsonPropertyName("inheritShared")] bool InheritShared,
        [property: JsonPropertyName("includeInSearchByDefault")] bool IncludeInSearchByDefault);

private static string? ValidateRelations(object? relations)
{
    switch (relations)
    {
        case null:
            return null;
        case JsonElement je:
            return ValidateRelationsElement(je);
        case JsonNode jn:
            return ValidateRelationsElement(JsonDocument.Parse(jn.ToJsonString()).RootElement);
        case IEnumerable<MemoryRelation> list:
            foreach (var rel in list)
            {
                if (rel is null || string.IsNullOrWhiteSpace(rel.Type) || string.IsNullOrWhiteSpace(rel.TargetId))
                {
                    return "invalid-relations: each relation needs non-empty type and targetId";
                }
            }
            return null;
        default:
            return "invalid-relations: must be an array of { type, targetId }";
    }
}

private static string? ValidateRelationsElement(JsonElement relations)
{
    if (relations.ValueKind == JsonValueKind.Undefined || relations.ValueKind == JsonValueKind.Null)
    {
        return null;
    }

    if (relations.ValueKind != JsonValueKind.Array)
    {
        return "invalid-relations: must be an array of { type, targetId }";
    }

    foreach (var rel in relations.EnumerateArray())
    {
        if (rel.ValueKind != JsonValueKind.Object)
        {
            return "invalid-relations: each relation must be an object";
        }

        if (!rel.TryGetProperty("type", out var typeProp) || typeProp.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(typeProp.GetString()))
        {
            return "invalid-relations: each relation needs a non-empty 'type'";
        }

        if (!rel.TryGetProperty("targetId", out var targetProp) || targetProp.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(targetProp.GetString()))
        {
            return "invalid-relations: each relation needs a non-empty 'targetId' (e.g., project:key)";
        }
    }

    return null;
}

private static string? ValidateSource(object? source)
{
    switch (source)
    {
        case null:
            return null;
        case JsonElement je:
            return ValidateSourceElement(je);
        case JsonNode jn:
            return ValidateSourceElement(JsonDocument.Parse(jn.ToJsonString()).RootElement);
        case MemorySource ms:
            return ValidateSourceFields(ms.Type, ms.Url, ms.Path, ms.Shard);
        default:
            return "invalid-source: must be an object with type/url/path/shard";
    }
}

private static string? ValidateSourceElement(JsonElement source)
{
    if (source.ValueKind == JsonValueKind.Undefined || source.ValueKind == JsonValueKind.Null)
    {
        return null;
    }

    if (source.ValueKind != JsonValueKind.Object)
    {
        return "invalid-source: must be a JSON object (type/url/path/shard)";
    }

    foreach (var prop in source.EnumerateObject())
    {
        var name = prop.Name;
        if (name is not "type" and not "url" and not "path" and not "shard")
        {
            return "invalid-source: allowed fields are type, url, path, shard";
        }
    }

    return null;
}

private static string? ValidateSourceFields(string? type, string? url, string? path, string? shard)
{
    // All optional; just ensure no unexpected structure
    return null;
}

}
