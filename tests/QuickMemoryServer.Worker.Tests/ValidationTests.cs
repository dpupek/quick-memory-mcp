using System;
using System.Reflection;
using System.Text.Json;
using QuickMemoryServer.Worker.Models;
using QuickMemoryServer.Worker.Services;
using Xunit;

public class ValidationTests
{
    private static MethodInfo GetMethod(string name) => typeof(MemoryMcpTools)
        .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static) ?? throw new InvalidOperationException($"Missing {name}");

    [Fact]
    public void ValidateRelations_AllowsNull()
    {
        var method = GetMethod("ValidateRelations");
        var result = method.Invoke(null, new object?[] { null }) as string;
        Assert.Null(result);
    }

    [Fact]
    public void ValidateRelations_RejectsNonArray()
    {
        var method = GetMethod("ValidateRelations");
        var json = JsonDocument.Parse("{\"type\":\"ref\"}").RootElement;
        var result = method.Invoke(null, new object?[] { json }) as string;
        Assert.Contains("invalid-relations", result);
    }

    [Fact]
    public void ValidateRelations_RejectsMissingFields()
    {
        var method = GetMethod("ValidateRelations");
        var json = JsonDocument.Parse("[{}]").RootElement;
        var result = method.Invoke(null, new object?[] { json }) as string;
        Assert.Contains("type", result ?? string.Empty);
    }

    [Fact]
    public void ValidateRelations_AcceptsValid()
    {
        var method = GetMethod("ValidateRelations");
        var json = JsonDocument.Parse("[{\"type\":\"ref\",\"targetId\":\"proj:key\"}]").RootElement;
        var result = method.Invoke(null, new object?[] { json }) as string;
        Assert.Null(result);
    }

    [Fact]
    public void ValidateSource_AllowsNull()
    {
        var method = GetMethod("ValidateSource");
        var result = method.Invoke(null, new object?[] { null }) as string;
        Assert.Null(result);
    }

    [Fact]
    public void ValidateSource_RejectsNonObject()
    {
        var method = GetMethod("ValidateSource");
        var json = JsonDocument.Parse("[1,2,3]").RootElement;
        var result = method.Invoke(null, new object?[] { json }) as string;
        Assert.Contains("invalid-source", result);
    }

    [Fact]
    public void ValidateSource_RejectsUnknownField()
    {
        var method = GetMethod("ValidateSource");
        var json = JsonDocument.Parse("{\"foo\":\"bar\"}").RootElement;
        var result = method.Invoke(null, new object?[] { json }) as string;
        Assert.Contains("allowed fields", result);
    }

    [Fact]
    public void ValidateSource_AcceptsKnownFields()
    {
        var method = GetMethod("ValidateSource");
        var json = JsonDocument.Parse("{\"type\":\"api\",\"url\":\"https://x\"}").RootElement;
        var result = method.Invoke(null, new object?[] { json }) as string;
        Assert.Null(result);
    }

    [Fact]
    public void TryPrepareEntry_AssignsProjectAndId()
    {
        var entry = new MemoryEntry { Project = string.Empty, Id = string.Empty, Kind = "note" };
        var success = MemoryMcpTools.TryPrepareEntry("projectA", entry, out var prepared, out var error);
        Assert.True(success);
        Assert.Null(error);
        Assert.Equal("projectA", prepared.Project);
        Assert.StartsWith("projectA:", prepared.Id, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryPrepareEntry_RejectsMismatchedProject()
    {
        var entry = new MemoryEntry { Project = "other", Id = "other:1", Kind = "note" };
        var success = MemoryMcpTools.TryPrepareEntry("projectA", entry, out _, out var error);
        Assert.False(success);
        Assert.Contains("project-mismatch", error);
    }
}
