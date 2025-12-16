using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using QuickMemoryServer.Worker.Services;

namespace QuickMemoryServer.Worker.Tests;

public sealed class SchemaServiceTests
{
    [Fact]
    public void MemoryEntrySchema_IncludesBodyTypeHint()
    {
#region Arrange
        var service = new SchemaService(NullLogger<SchemaService>.Instance);
#endregion

#region Assert (initial state)
        Assert.False(string.IsNullOrWhiteSpace(service.Json));
#endregion

#region Act
        using var doc = JsonDocument.Parse(service.Json);
#endregion

#region Assert (post state)
        var properties = doc.RootElement
            .GetProperty("definitions")
            .GetProperty("MemoryEntry")
            .GetProperty("properties");

        Assert.True(properties.TryGetProperty("bodyTypeHint", out var bodyTypeHint));
        Assert.Equal("string", bodyTypeHint.GetProperty("type").GetString());

        var description = bodyTypeHint.GetProperty("description").GetString();
        Assert.NotNull(description);
        Assert.Contains("text", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("yaml", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("toml", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("csv", description, StringComparison.OrdinalIgnoreCase);
#endregion
    }
}
