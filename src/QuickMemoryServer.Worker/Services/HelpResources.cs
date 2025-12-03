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

## Configure the client
- Set `[mcp_servers.quick-memory] url = "http://localhost:5080/mcp"` and `experimental_use_rmcp_client = true` in your Codex config.
- Provide an API key via `bearer_token_env_var = "QMS_API_KEY"` (export QMS_API_KEY before launching Codex).

## Tools
- `listProjects` – lists endpoints/projects you can access.
- `searchEntries` – text/vector search within a project (optionally include shared).
- `getEntry` / `listEntries` – fetch one or all entries.
- `upsertEntry` / `patchEntry` / `deleteEntry` – mutate entries (requires sufficient tier; permanent entries need Admin).
- `relatedEntries` – walk the related graph.
- `requestBackup` – queue a backup for a project (Admin only).
- `health` – returns server health report.

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
}
