using System.Text.Json.Serialization;

namespace QuickMemoryServer.Worker.Models;

internal record LoginRequest([property: JsonPropertyName("apiKey")] string? ApiKey);
internal record RawConfigRequest([property: JsonPropertyName("content")] string? Content);
