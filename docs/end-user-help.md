# Quick Memory Server End-User Help

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
- Set up the client-side Codex configuration in `~/.codex/config.toml` (or the workspace-level config) with the MCP streamable transport:
  ```toml
  [mcp_servers.quick-memory]
  url = "http://localhost:5080/mcp"
  experimental_use_rmcp_client = true
  bearer_token_env_var = "QMS_API_KEY"
  ```
  Export the API key you want to use before launching Codex:
  ```powershell
  $env:QMS_API_KEY = "your-api-key-here"
  ```
  On Linux/macOS: `export QMS_API_KEY=your-api-key-here`.
- Use the Overview tab in the admin SPA to verify `/health`, copy the user-specific snippet, and ensure the listed endpoints match the ones you configured for Codex. This page also contains the current API key snippet so you can paste it directly into `.codex/config.toml` or the TOML service config when rotating keys.
