using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    private const string PromptsEndpointKey = "prompts-repository";

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

    if (!TryPrepareEntry(endpoint, entry, out var prepared, out var prepareError))
    {
        return ErrorResult(prepareError ?? "invalid-entry");
    }
    entry = prepared;

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
        return new { updated = true, id = entry.Id };
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

        var promptValidationError = ValidatePromptEntry(updated);
        if (promptValidationError is not null)
        {
            return ErrorResult(promptValidationError);
        }

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
        if (string.Equals(endpoint, PromptsEndpointKey, StringComparison.OrdinalIgnoreCase))
        {
            return ErrorResult("prompt-delete-disallowed: prompts in prompts-repository should be retired or edited, not hard-deleted.");
        }

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

[McpServerTool(Name = "coldStart", Title = "Cold start snapshot", ReadOnly = true)]
[McpMeta("description", "Return curated cold-start entries plus recent activity for a project.")]
[McpMeta("tier", "reader")]
public static object ColdStart(
    string endpoint,
    string? epicSlug,
    MemoryRouter router)
{
    if (router.ResolveStore(endpoint) is not MemoryStore store)
    {
        return ErrorResult($"Endpoint '{endpoint}' is not available.");
    }

    var snapshot = store.Snapshot();

    var coldStartEntries = snapshot
        .Where(e =>
            e.Tags != null &&
            e.Tags.Any(t => string.Equals(t, "category:cold-start", StringComparison.OrdinalIgnoreCase)) &&
            (string.Equals(e.CurationTier, "curated", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(e.CurationTier, "canonical", StringComparison.OrdinalIgnoreCase)))
        .ToArray();

    var recentQuery = snapshot.AsEnumerable();
    if (!string.IsNullOrWhiteSpace(epicSlug))
    {
        recentQuery = recentQuery.Where(e =>
            !string.IsNullOrWhiteSpace(e.EpicSlug) &&
            string.Equals(e.EpicSlug, epicSlug, StringComparison.OrdinalIgnoreCase));
    }

    var recentEntries = recentQuery
        .OrderByDescending(e => e.Timestamps?.UpdatedUtc ?? e.Timestamps?.CreatedUtc ?? DateTimeOffset.UnixEpoch)
        .ThenBy(e => e.Id)
        .Take(20)
        .ToArray();

    return new ColdStartResponse(
        Endpoint: endpoint,
        EpicSlug: epicSlug,
        ColdStartEntries: coldStartEntries,
        RecentEntries: recentEntries);
}

[McpServerTool(Name = "health", Title = "Health report", ReadOnly = true)]
[McpMeta("description", "Expose stores, uptime, and issue counts that the SPA surfaces.")]
[McpMeta("tier", "reader")]
public static HealthReport GetHealth(HealthReporter healthReporter)
    {
        return healthReporter.GetReport();
    }

[McpServerTool(Name = "listPromptTemplates", Title = "List curated prompt templates", ReadOnly = true)]
[McpMeta("description", "List curated MCP prompt templates backed by entries in the prompts-repository endpoint.")]
[McpMeta("tier", "reader")]
public static PromptListResponse ListPromptTemplates(MemoryRouter router)
{
    if (router.ResolveStore(PromptsEndpointKey) is not MemoryStore store)
    {
        return new PromptListResponse(Array.Empty<PromptListItem>());
    }

    var entries = store.Snapshot()
        .Where(e =>
            string.Equals(e.Kind, "prompt", StringComparison.OrdinalIgnoreCase) &&
            e.Tags.Any(t => string.Equals(t, "prompt-template", StringComparison.OrdinalIgnoreCase)))
        .ToArray();

    var prompts = new List<PromptListItem>();

    foreach (var entry in entries)
    {
        if (!TryGetPromptBody(entry, out var text))
        {
            continue;
        }

        if (!TryExtractPromptArgs(text, out var args, out var strippedBody))
        {
            // Even if there is no args block or it fails to parse,
            // we still expose the prompt with zero arguments.
        }

        var categories = entry.Tags
            .Where(t => t.StartsWith("category:", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Substring("category:".Length))
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToArray();

        prompts.Add(new PromptListItem(
            Name: entry.Id,
            Title: entry.Title ?? entry.Id,
            Categories: categories,
            Arguments: args));
    }

    return new PromptListResponse(prompts);
}

[McpServerTool(Name = "getPromptTemplate", Title = "Get a curated prompt template", ReadOnly = true)]
[McpMeta("description", "Resolve a curated prompt template by name with argument substitution.")]
[McpMeta("tier", "reader")]
public static object GetPromptTemplate(
    string name,
    Dictionary<string, string>? arguments,
    MemoryRouter router)
{
    if (string.IsNullOrWhiteSpace(name))
    {
        return ErrorResult("prompt-name-required");
    }

    if (router.ResolveStore(PromptsEndpointKey) is not MemoryStore store)
    {
        return ErrorResult("prompts-repository-not-configured");
    }

    var entry = store.Snapshot()
        .FirstOrDefault(e => string.Equals(e.Id, name, StringComparison.OrdinalIgnoreCase));

    if (entry is null)
    {
        return ErrorResult("prompt-not-found");
    }

    if (!TryGetPromptBody(entry, out var text))
    {
        return ErrorResult("prompt-body-missing");
    }

    if (!TryExtractPromptArgs(text, out var args, out var templateBody))
    {
        // No args block; treat as zero-argument prompt.
        args = Array.Empty<PromptArgument>();
        templateBody = text;
    }

    var requiredArgs = args.Where(a => a.Required).Select(a => a.Name).ToArray();
    var missing = requiredArgs
        .Where(r => arguments == null || !arguments.ContainsKey(r) || string.IsNullOrWhiteSpace(arguments[r]))
        .ToArray();

    if (missing.Length > 0)
    {
        return ErrorResult("missing-arguments: " + string.Join(",", missing));
    }

    var resolved = templateBody;
    if (arguments is not null)
    {
        foreach (var arg in args)
        {
            if (!arguments.TryGetValue(arg.Name, out var value) || value is null)
            {
                continue;
            }

            var placeholder = "{{" + arg.Name + "}}";
            resolved = resolved.Replace(placeholder, value, StringComparison.Ordinal);
        }
    }

    var categories = entry.Tags
        .Where(t => t.StartsWith("category:", StringComparison.OrdinalIgnoreCase))
        .Select(t => t.Substring("category:".Length))
        .Where(t => !string.IsNullOrWhiteSpace(t))
        .ToArray();

    var response = new PromptGetResponse(
        Name: entry.Id,
        Title: entry.Title ?? entry.Id,
        Categories: categories,
        Arguments: args,
        Messages: new[]
        {
            new PromptMessage(
                Role: "user",
                Content: new[]
                {
                    new PromptMessageContent(
                        Type: "text",
                        Text: resolved)
                })
        });

    return response;
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

    public sealed record ColdStartResponse(
        [property: JsonPropertyName("endpoint")] string Endpoint,
        [property: JsonPropertyName("epicSlug")] string? EpicSlug,
        [property: JsonPropertyName("coldStartEntries")] IReadOnlyList<MemoryEntry> ColdStartEntries,
        [property: JsonPropertyName("recentEntries")] IReadOnlyList<MemoryEntry> RecentEntries);

    public sealed record PromptArgument(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("required")] bool Required);

    public sealed record PromptListItem(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("categories")] IReadOnlyList<string> Categories,
        [property: JsonPropertyName("arguments")] IReadOnlyList<PromptArgument> Arguments);

    public sealed record PromptListResponse(
        [property: JsonPropertyName("prompts")] IReadOnlyList<PromptListItem> Prompts);

    public sealed record PromptMessageContent(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("text")] string Text);

    public sealed record PromptMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] IReadOnlyList<PromptMessageContent> Content);

    public sealed record PromptGetResponse(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("categories")] IReadOnlyList<string> Categories,
        [property: JsonPropertyName("arguments")] IReadOnlyList<PromptArgument> Arguments,
        [property: JsonPropertyName("messages")] IReadOnlyList<PromptMessage> Messages);

    private static string? ValidatePromptEntry(MemoryEntry entry)
    {
        if (!string.Equals(entry.Project, PromptsEndpointKey, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!string.Equals(entry.Kind, "prompt", StringComparison.OrdinalIgnoreCase))
        {
            return "prompts-repository entries must use kind='prompt'.";
        }

        if (entry.Tags is null || !entry.Tags.Any(t => string.Equals(t, "prompt-template", StringComparison.OrdinalIgnoreCase)))
        {
            return "prompts-repository entries must include the 'prompt-template' tag.";
        }

        if (!TryGetPromptBody(entry, out var text))
        {
            return "prompts-repository entries must have a body with text or { text: \"...\" }.";
        }

        const string fence = "```prompt-args";
        if (text.IndexOf(fence, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            if (!TryExtractPromptArgs(text, out _, out _))
            {
                return "invalid prompt-args block: must be valid JSON inside ```prompt-args ``` fencing.";
            }
        }

        return null;
    }

    private static bool TryGetPromptBody(MemoryEntry entry, out string text)
    {
        text = string.Empty;

        if (entry.Body is null)
        {
            return false;
        }

        if (entry.Body is JsonValue value && value.TryGetValue(out string? str) && !string.IsNullOrWhiteSpace(str))
        {
            text = str;
            return true;
        }

        if (entry.Body is JsonObject obj &&
            obj.TryGetPropertyValue("text", out var textNode) &&
            textNode is JsonValue tv &&
            tv.TryGetValue(out string? textValue) &&
            !string.IsNullOrWhiteSpace(textValue))
        {
            text = textValue;
            return true;
        }

        return false;
    }

    private static bool TryExtractPromptArgs(
        string body,
        out IReadOnlyList<PromptArgument> arguments,
        out string strippedBody)
    {
        const string fence = "```prompt-args";
        arguments = Array.Empty<PromptArgument>();
        strippedBody = body;

        var fenceStart = body.IndexOf(fence, StringComparison.OrdinalIgnoreCase);
        if (fenceStart < 0)
        {
            return false;
        }

        var firstNewline = body.IndexOf('\n', fenceStart + fence.Length);
        if (firstNewline < 0)
        {
            return false;
        }

        var fenceEnd = body.IndexOf("```", firstNewline + 1, StringComparison.Ordinal);
        if (fenceEnd < 0)
        {
            return false;
        }

        var jsonSegment = body.Substring(firstNewline + 1, fenceEnd - (firstNewline + 1)).Trim();
        try
        {
            var parsed = JsonSerializer.Deserialize<List<PromptArgument>>(jsonSegment, JsonOptions);
            if (parsed is null)
            {
                return false;
            }

            arguments = parsed;
        }
        catch
        {
            return false;
        }

        var before = body.Substring(0, fenceStart);
        var after = body.Substring(fenceEnd + 3);
        strippedBody = (before + after).Trim();

        return true;
    }

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

internal static bool TryPrepareEntry(string endpoint, MemoryEntry entry, out MemoryEntry prepared, out string? error)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
    ArgumentNullException.ThrowIfNull(entry);

    var project = string.IsNullOrWhiteSpace(entry.Project) ? endpoint : entry.Project;
    if (!string.Equals(project, endpoint, StringComparison.OrdinalIgnoreCase))
    {
        prepared = entry;
        error = "project-mismatch: set entry.project to the endpoint you are calling";
        return false;
    }

    var normalized = entry;
    if (!string.Equals(entry.Project, project, StringComparison.OrdinalIgnoreCase))
    {
        normalized = normalized with { Project = project };
    }

    if (string.IsNullOrWhiteSpace(normalized.Id))
    {
        var generatedId = $"{project}:{Guid.NewGuid():N}";
        normalized = normalized with { Id = generatedId };
    }

    var promptValidationError = ValidatePromptEntry(normalized);
    if (promptValidationError is not null)
    {
        prepared = entry;
        error = promptValidationError;
        return false;
    }

    prepared = normalized;
    error = null;
    return true;
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
