using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace QuickMemoryServer.Worker.Services;

public sealed class SchemaService
{
    private readonly string _payload;
    private readonly string _etag;

    public SchemaService(ILogger<SchemaService> logger)
    {
        var document = new JsonObject
        {
            ["schemaVersion"] = "1.0",
            ["generatedUtc"] = DateTimeOffset.UtcNow,
            ["definitions"] = new JsonObject
            {
                ["MemoryEntry"] = BuildMemoryEntrySchema(),
                ["SearchRequest"] = BuildSearchRequestSchema()
            }
        };

        var options = new JsonSerializerOptions { WriteIndented = true };
        _payload = document.ToJsonString(options);
        _etag = ComputeEtag(_payload);
        logger.LogInformation("SchemaServlet generated /docs/schema with ETag {Etag}", _etag);
    }

    public string Json => _payload;

    public string ETag => _etag;

    private static JsonObject BuildMemoryEntrySchema()
    {
        var properties = new JsonObject
        {
            ["schemaVersion"] = CreateProperty("integer"),
            ["id"] = CreateProperty("string"),
            ["project"] = CreateProperty("string"),
            ["kind"] = CreateProperty("string"),
            ["title"] = CreateProperty("string"),
            ["body"] = CreateProperty("object"),
            ["bodyTypeHint"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Optional hint for how editors/agents should interpret the body payload (syntax mode only, not validation). Recommended values: text, json, markdown, html, xml, yaml, toml, csv."
            },
            ["tags"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = CreateProperty("string")
            },
            ["source"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["type"] = CreateProperty("string"),
                    ["path"] = CreateProperty("string"),
                    ["url"] = CreateProperty("string"),
                    ["shard"] = CreateProperty("string")
                }
            },
            ["embedding"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = CreateProperty("number")
            },
            ["keywords"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = CreateProperty("string")
            },
            ["relations"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["type"] = CreateProperty("string"),
                        ["targetId"] = CreateProperty("string"),
                        ["weight"] = CreateProperty("number")
                    }
                }
            },
            ["timestamps"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["createdUtc"] = CreateProperty("string", "date-time"),
                    ["updatedUtc"] = CreateProperty("string", "date-time"),
                    ["sourceUtc"] = CreateProperty("string", "date-time")
                }
            },
            ["ttlUtc"] = CreateProperty("string", "date-time"),
            ["confidence"] = CreateProperty("number"),
            ["curationTier"] = CreateProperty("string"),
            ["epicSlug"] = CreateProperty("string"),
            ["epicCase"] = CreateProperty("string"),
            ["isPermanent"] = CreateProperty("boolean"),
            ["pinned"] = CreateProperty("boolean")
        };

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = new JsonArray("schemaVersion", "id", "project", "kind")
        };
    }

    private static JsonObject BuildSearchRequestSchema()
    {
        var properties = new JsonObject
        {
            ["text"] = CreateProperty("string"),
            ["embedding"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = CreateProperty("number")
            },
            ["maxResults"] = CreateProperty("integer"),
            ["includeShared"] = CreateProperty("boolean"),
            ["tags"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = CreateProperty("string")
            }
        };

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties
        };
    }

    private static JsonObject CreateProperty(string type, string? format = null)
    {
        var node = new JsonObject { ["type"] = type };
        if (!string.IsNullOrEmpty(format))
        {
            node["format"] = format;
        }

        return node;
    }

    private static string ComputeEtag(string payload)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToBase64String(hash);
    }
}
