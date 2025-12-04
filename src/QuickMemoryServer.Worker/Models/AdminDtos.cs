using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace QuickMemoryServer.Worker.Models;

public sealed record AdminUserRequest(
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("apiKey")] string ApiKey,
    [property: JsonPropertyName("defaultTier")] string DefaultTier);

public sealed record PermissionUpdateRequest(
    [property: JsonPropertyName("assignments")] Dictionary<string, string> Assignments);

public sealed record PermissionBulkUpdateRequest(
    [property: JsonPropertyName("projects")] string[] Projects,
    [property: JsonPropertyName("overrides")] Dictionary<string, string?> Overrides);

public sealed record AdminEndpointRequest(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("storagePath")] string StoragePath,
    [property: JsonPropertyName("includeInSearchByDefault")] bool IncludeInSearchByDefault,
    [property: JsonPropertyName("inheritShared")] bool InheritShared);
