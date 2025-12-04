using System.Threading.Tasks;
using System.IO;
using ModelContextProtocol.Server;
using System;

namespace QuickMemoryServer.Worker.Services;

[McpServerResourceType]
internal static class HelpResources
{
    [McpServerResource(
        Name = "help",
        Title = "Quick Memory MCP Help",
        MimeType = "text/markdown",
        UriTemplate = "resource://quick-memory/help")]
    public static Task<string> GetHelpAsync()
    {
        var header = """
# Quick Memory MCP Help

## Quick start
- listProjects → pick an endpoint you’re allowed to use.
- listRecentEntries (endpoint) → browse the latest updates without a query.
- searchEntries (endpoint, text, includeShared) → focused retrieval.
- getEntry / listEntries → fetch specific/all entries.
- relatedEntries (id) → explore graph links.
- upsertEntry / patchEntry / deleteEntry → mutate (permanent requires Admin).
- requestBackup (Admin) → queue backup.
- health → server status.

## Configure the client
- Set `[mcp_servers.quick-memory] url = "http://localhost:5080/mcp"` and `experimental_use_rmcp_client = true` in your Codex config.
- Provide an API key via `bearer_token_env_var = "QMS_API_KEY"` (export QMS_API_KEY before launching Codex).

## API key tiers
- Reader: read-only tools.
- Curator: edit non-permanent entries.
- Admin: full access including backups and permanent entries.

## HTTP endpoints
- `/health` – JSON health report.
- `/mcp/{endpoint}/searchEntries` – direct HTTP MCP routes (X-Api-Key required).
- `/admin/help/end-user` – rendered end-user help.

## Recipes and examples
The latest recipes live in `docs/agent-usage.md` (copied alongside the binaries by the installer). The contents are appended below when available.
""";

        var agentUsagePath = Path.Combine(AppContext.BaseDirectory, "docs", "agent-usage.md");
        if (File.Exists(agentUsagePath))
        {
            try
            {
                var recipes = File.ReadAllText(agentUsagePath);
                return Task.FromResult(header + "\n\n" + recipes);
            }
            catch
            {
                // fall through to header-only if read fails
            }
        }

        return Task.FromResult(header);
    }

    [McpServerResource(
        Name = "end-user-help",
        Title = "Quick Memory End-User Help",
        MimeType = "text/markdown",
        UriTemplate = "resource://quick-memory/end-user-help")]
    public static Task<string> GetEndUserHelpAsync()
    {
        var header = "# Quick Memory End-User Help\n\nSee the admin SPA Help tab for the rendered version.";
        var path = Path.Combine(AppContext.BaseDirectory, "docs", "end-user-help.md");
        if (File.Exists(path))
        {
            try
            {
                var content = File.ReadAllText(path);
                return Task.FromResult(content);
            }
            catch
            {
                // fall through to header
            }
        }

        return Task.FromResult(header);
    }

    [McpServerResource(
        Name = "cheatsheet",
        Title = "Quick Memory MCP Cheatsheet",
        MimeType = "text/markdown",
        UriTemplate = "resource://quick-memory/cheatsheet")]
    public static Task<string> GetCheatsheetAsync()
    {
        var text = """
# Quick Memory MCP Cheatsheet

- Base URL: http://localhost:5080/mcp (header: X-Api-Key)
- Discover scope: call `listProjects` (projects returned = allowed endpoints).
- Cold start browse: `listRecentEntries` (top N by updated time).
- Search text/vector: `searchEntries` with { text, maxResults, includeShared }.
- Fetch one/all: `getEntry`, `listEntries`.
- Mutate: `upsertEntry`, `patchEntry` (permanent requires Admin).
- Graph: `relatedEntries` with { id, maxHops }.
- Backup: `requestBackup` (Admin, mode: differential|full).
- Health: `health` for overall/server issues.
- Config snippets + recipes live in `resource://quick-memory/help` and `resource://quick-memory/end-user-help`.
""";
        return Task.FromResult(text);
    }

[McpServerResource(
    Name = "entry-fields",
    Title = "MemoryEntry Field Guide",
    MimeType = "text/markdown",
    UriTemplate = "resource://quick-memory/entry-fields")]
    public static Task<string> GetEntryFieldsAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "docs", "agent-usage.md");
        if (!File.Exists(path))
        {
            return Task.FromResult("# MemoryEntry Field Reference\n\nSee `/admin/help/agent#memoryentry-field-reference` for the latest table.");
        }

        try
        {
            var markdown = File.ReadAllText(path);
            const string marker = "## MemoryEntry field reference";
            var start = markdown.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                return Task.FromResult("# MemoryEntry Field Reference\n\nSection not found in agent-usage.md.");
            }

            var nextHeader = markdown.IndexOf("\n## ", start + marker.Length, StringComparison.Ordinal);
            var section = nextHeader >= 0
                ? markdown.Substring(start, nextHeader - start)
                : markdown.Substring(start);

            return Task.FromResult(section.Trim());
        }
        catch
        {
            return Task.FromResult("# MemoryEntry Field Reference\n\nUnable to load agent-usage.md.");
        }
    }

    [McpServerResource(
        Name = "codex-workspace",
        Title = "Codex Workspace Guide",
        MimeType = "text/markdown",
        UriTemplate = "resource://quick-memory/codex-workspace")]
    public static Task<string> GetCodexWorkspaceGuideAsync()
    {
        var fallback = "# Codex Workspace Guide\n\nOpen the admin SPA Help tab to read the rendered version.";
        var path = Path.Combine(AppContext.BaseDirectory, "docs", "codex-workspace-guide.md");
        if (File.Exists(path))
        {
            try
            {
                var content = File.ReadAllText(path);
                return Task.FromResult(content);
            }
            catch
            {
                // fall through
            }
        }

        return Task.FromResult(fallback);
    }
}
