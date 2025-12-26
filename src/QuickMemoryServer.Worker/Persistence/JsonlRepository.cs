using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using QuickMemoryServer.Worker.Models;
using QuickMemoryServer.Worker.Validation;

namespace QuickMemoryServer.Worker.Persistence;

public sealed class JsonlRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private readonly MemoryEntryValidator _validator;
    private readonly ILogger<JsonlRepository> _logger;

    public JsonlRepository(MemoryEntryValidator validator, ILogger<JsonlRepository> logger)
    {
        _validator = validator;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MemoryEntry>> LoadAsync(string path, int embeddingDimensions, CancellationToken cancellationToken)
    {
        _ = path ?? throw new ArgumentNullException(nameof(path));

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (!File.Exists(path))
        {
            await using var created = File.Create(path);
            return Array.Empty<MemoryEntry>();
        }

        var entries = new List<MemoryEntry>();
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false);

        var lineNumber = 0;
        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync();
            lineNumber++;

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var entry = JsonSerializer.Deserialize<MemoryEntry>(line, SerializerOptions)
                            ?? throw new JsonException("Deserialized null entry.");

                entries.Add(_validator.Normalize(entry, embeddingDimensions));
            }
            catch (Exception ex) when (ex is JsonException or MemoryValidationException)
            {
                _logger.LogError(ex, "Failed to parse memory entry at line {Line} in {Path}", lineNumber, path);
                throw new JsonlParseException(path, lineNumber, ex);
            }
        }

        return entries;
    }

    public async Task SaveAsync(string path, IEnumerable<MemoryEntry> entries, int embeddingDimensions, CancellationToken cancellationToken)
    {
        _ = path ?? throw new ArgumentNullException(nameof(path));
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var materialized = entries.Select(e => _validator.Normalize(e, embeddingDimensions)).ToArray();
        var tempPath = path + ".tmp";

        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        await using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
        {
            foreach (var entry in materialized)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var jsonNode = JsonSerializer.SerializeToNode(entry, SerializerOptions) as JsonObject;
                if (jsonNode is not null)
                {
                    jsonNode.Remove("project");
                    var json = jsonNode.ToJsonString(SerializerOptions);
                    await writer.WriteLineAsync(json.AsMemory(), cancellationToken);
                    continue;
                }

                var fallbackJson = JsonSerializer.Serialize(entry, SerializerOptions);
                await writer.WriteLineAsync(fallbackJson.AsMemory(), cancellationToken);
            }
        }

        File.Move(tempPath, path, overwrite: true);
    }
}
