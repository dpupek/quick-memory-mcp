# Quick Memory Server End-User Help

- [MCP short commands](#mcp-short-commands)
- [Admin SPA walkthrough](#admin-spa-walkthrough)
- [Installing in Codex](#installing-in-codex)
- [Troubleshooting](#troubleshooting)

Use these steps when accessing the MCP memory server or the embedded admin SPA.

## MCP short commands
1. Authenticate every request with `X-Api-Key: <your key>`.
2. List endpoints: `GET /admin/endpoints`.
3. Check health: `GET /health`; take note of `status`, `stores`, and `issues`.
4. Search: `POST /mcp/{endpoint}/searchEntries` with optional `text`, `tags`, and `embedding`.
5. Fetch a single entry: `GET /mcp/{endpoint}/entries/{id}`.
6. Upsert: `POST /mcp/{endpoint}/entries` with the entry payload (see schema summary in the SPA).
7. Patch: `PATCH /mcp/{endpoint}/entries/{id}` to change tags, title, tier, body, etc.
8. Related entries: `POST /mcp/{endpoint}/relatedEntries` with `id` + `maxHops` to walk the relation graph.
9. Backup: `POST /mcp/{endpoint}/backup` with `{ "mode": "differential" }` (admin tier).

## Admin SPA walkthrough
1. Visit `/`, enter your API key, and the SPA will fetch the endpoints you have permissions on.
2. The Projects tab shows each endpoint's metadata and lets you store `projectMetadata` entries that capture storage paths, include/shared settings, and descriptions.
3. Entities provides inline search and editing plus a form to create new entries (title, kind, tags, tier, body). After creating, the list refreshes automatically.
4. Users lets you add/update API keys and assign per-endpoint tiers (used by Codex or service bots).
5. The Help tabs surface this page plus the agent usage doc so you never leave the console.

## Installing in Codex
- Copy the `QuickMemoryServer.sample.toml` snippet, fill in your API key, and paste it into `QuickMemoryServer.toml` (same directory as the service exe). Restart the worker when you edit the file directly; the SPA Users tab writes this file for you after CRUD operations and each change is reloaded automatically.
- Codex currently accesses HTTP MCP servers through the `mcp-remote` bridge. Add this block to `~/.codex/config.toml` (or your workspace-level config):
  ```toml
  [mcp_servers.quick-memory]
  command = "npx"
  args = [
    "mcp-remote@latest",
    "http://localhost:5080/mcp",
    "--header","X-Api-Key:$AUTH_TOKEN",
    "--allow-http",
    "--debug"
  ]
  env = { AUTH_TOKEN = "${env:AUTH_TOKEN}" }
  ```
  - Export `AUTH_TOKEN` before launching Codex (`$env:AUTH_TOKEN="your-api-key"` on PowerShell, `export AUTH_TOKEN=your-api-key` on Bash/Zsh).
  - `mcp-remote` stores auth metadata under `~/.mcp-auth`. Delete the corresponding folder if you rotate keys and need the bridge to prompt for the new value.
- To scope an agent to a single project, issue it an API key that only has permissions on that project (configure via SPA Users/Permissions). The MCP base URL stays the same (`http://localhost:5080/mcp`); disallowed projects will be filtered by `listProjects` and blocked on use.
- **Workspace-specific Codex config (per-project keys)**:
  1. Create a dedicated API key (e.g., `projectA-bot`) and grant it access to exactly one project in the admin SPA.
  2. Inside that project’s repo, create `.codex/config.toml`. Codex automatically prefers this file over `~/.codex/config.toml` when you open the folder.
  3. Drop the bridge definition directly in the workspace config so the key never leaves that repo:
     ```toml
     # <workspace>/.codex/config.toml
     [mcp_servers.quick-memory]
     command = "npx"
     args = [
       "mcp-remote@latest",
       "http://localhost:5080/mcp",
       "--header","X-Api-Key:${env:PROJECT_A_KEY}",
       "--allow-http"
     ]
     env = { PROJECT_A_KEY = "paste-the-projectA-key-here" }
     ```
     - You can also keep secrets out of source control by referencing a workspace-specific env var instead of inlining the value (e.g., run `export PROJECT_A_KEY=...` before launching Codex, or store it in `.codex/secrets.toml` which Git ignores by default).
     - Repeat this pattern for other projects by cloning the repo, creating `.codex/config.toml`, and pointing it at an API key that only knows about that project. Codex will automatically limit the MCP tools exposed in that workspace to whatever the API key can see.
- Always add `.codex/` (and any `.codex/*.toml` secrets) to your project’s `.gitignore` so API keys never end up in version control. If the file already exists, append:
  ```
  # Codex workspace config
  .codex/
  ```
- Need a deeper walkthrough? See [Codex workspace guide](codex-workspace-guide.md) for screenshots, timeout knobs, and environment-variable tips.
- When calling `upsertEntry`, leave `id` empty to let the server generate a stable `<project>:<guid>` identifier automatically.
- Use the Overview tab in the admin SPA to verify `/health`, copy the user-specific snippet, and ensure the listed endpoints match the ones you configured for Codex. This page also contains the current API key snippet so you can rehydrate `.codex/config.toml` or `QuickMemoryServer.toml` when rotating keys.

## Troubleshooting
- **401 from SPA or MCP tools**: your session probably expired. Re-enter the API key (SPA) or restart Codex so `mcp-remote` re-reads the key. Check that the key still exists in the Users tab and retains the right tiers.
- **“Endpoint not found” in Codex**: the API key isn’t scoped to that project. Confirm project overrides in the Project Permissions blade and re-run `/admin/permissions` to ensure the user appears.
- **Installer warns about missing ONNX/JSONL files**: create placeholder `Models/` and `MemoryStores/` files or adjust `QuickMemoryServer.toml` paths to match your deployment layout.
- **`mcp-remote` keeps using an old key**: delete `%USERPROFILE%\.mcp-auth\*quick-memory*` (Windows) or `~/.mcp-auth/...` (macOS/Linux) so the bridge prompts for the new secret.
- **Audit logs seem empty**: only permission edits write to `quick-memory-audit-*.log` / `.db`. Perform a change in Project Permissions (or bulk override) to generate entries, then query the SQLite DB.
