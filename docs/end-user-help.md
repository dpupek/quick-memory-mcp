# Quick Memory Server End-User Help

- [MCP short commands](#mcp-short-commands)
- [Prompt recipes](#prompt-recipes)
- [Admin Web UI walkthrough](#admin-web-ui-walkthrough)
- [Installing in Codex](#installing-in-codex)
- [Troubleshooting](#troubleshooting)

Use these steps when accessing the Quick Memory MCP server or the Admin Web UI (browser).

## Codex MCP quick start (global config)
1. Install the Quick Memory service and confirm `GET /health` returns `"status": "Healthy"`.
2. From a WSL terminal, install `mcp-proxy` with `uv tool install mcp-proxy` so it is on your Linux `PATH`.
3. Open `~/.codex/config.toml` and add:
   ```toml
   [mcp_servers.quick-memory]
   command = "mcp-proxy"
   args = [
     "http://localhost:5080/mcp",
     "--transport", "streamablehttp",
     "--no-verify-ssl",
     "--headers", "X-Api-Key", "<your-api-key>",
     "--stateless"
   ]
   timeout_ms = 60000
   startup_timeout_ms = 60000
   ```
4. In the Admin Web UI, issue a project-limited API key (Users + Project Permissions) and paste it in place of `<your-api-key>`.
5. Restart Codex, verify the **Quick Memory** MCP server is listed, then call `listProjects` to confirm the endpoints (projects) that key can see.

`mcp-proxy` also supports a `--debug` flag for verbose logging if you
need to troubleshoot MCP startup or HTTP calls. For more detailed
Codex configuration patterns (multiple projects, env vars), see the
Codex MCP guide (`docs/codex-workspace-guide.md` or
`resource://quick-memory/codex-workspace`).

## MCP short commands
1. Authenticate every request with `X-Api-Key: <your key>`.
2. List projects (endpoints): `GET /admin/endpoints`.
3. Check health: `GET /health`; take note of `status`, `stores`, and `issues`.
4. Search: `POST /mcp/{endpoint}/searchEntries` with optional `text`, `tags`, and `embedding`.
5. Fetch a single entry: `GET /mcp/{endpoint}/entries/{id}`.
6. Upsert: `POST /mcp/{endpoint}/entries` with the entry payload (see the schema summary in the Admin Web UI).
7. Patch: `PATCH /mcp/{endpoint}/entries/{id}` to change tags, title, tier, body, etc.
8. Related entries: `POST /mcp/{endpoint}/relatedEntries` with `id` + `maxHops` to walk the relation graph.
9. Backup: `POST /mcp/{endpoint}/backup` with `{ "mode": "differential" }` (admin tier).

## Prompt recipes

These are example prompts you can paste into Codex or ChatGPT to get an
agent using Quick Memory effectively.

### First-time setup prompt

> "Please read **all** Quick Memory resources
> (`resource://quick-memory/help`, `resource://quick-memory/entry-fields`,
> `resource://quick-memory/codex-workspace`). I want you to start using
> Quick Memory to store and retrieve context between sessions and
> between agents. Summarize how entries are structured (id, project,
> kind, tags, body, confidence, curationTier, isPermanent) and propose
> a default tagging pattern for this project."

### Cold start prompt (new session)

> "Before doing anything else, call `listProjects` and
> `listRecentEntries { endpoint: \"<project-key>\", maxResults: 20 }` to
> catch up on the most recent entries. Summarize what you see and point
> out any gaps you think we should fill with new entries (tests,
> deployment, UX quirks, etc.). Then suggest which new entries we should
> create today."

### Recording a new lesson

> "We just debugged an issue. Draft an `upsertEntry` payload for endpoint
> `<project-key>` that captures:
> – a concise title,
> – `kind` (fact, procedure, or decision),
> – 3–6 useful tags,
> – a body that includes steps, root cause, and validation,
> – any relations to existing entries by id.
> Keep `isPermanent` false unless this is a canonical rule we never
> expect to change."

### Using Quick Memory during investigations

> "Before proposing changes, search Quick Memory with
> `searchEntries { endpoint: \"<project-key>\", text: \"<topic>\", maxResults:20 }`.
> Summarize any relevant entries, note prior decisions, and only then
> propose next steps. If nothing is found, explicitly say so and suggest
> a new entry we should create when we are done."

## Admin Web UI walkthrough
1. Visit `/`, enter your API key, and the Admin Web UI will fetch the endpoints you have permissions on.
2. The Projects tab shows each endpoint's metadata and lets you store `projectMetadata` entries that capture storage paths, include/shared settings, and descriptions.
3. Entities provides inline search and editing plus a form to create new entries (title, kind, tags, tier, body). After creating, the list refreshes automatically.
4. Users lets you add/update API keys and assign per-endpoint tiers (used by Codex or service bots).
5. The Help tabs surface this page plus the agent usage doc so you never leave the console.

## Installing in Codex
- Copy the `QuickMemoryServer.sample.toml` snippet, fill in your API key, and paste it into `QuickMemoryServer.toml` (same directory as the service exe). Restart the worker when you edit the file directly; the Admin Web UI Users tab writes this file for you after CRUD operations and each change is reloaded automatically.
- Codex accesses the Quick Memory MCP server using globally configured MCP
  servers in `~/.codex/config.toml`. For up-to-date examples using
  `mcp-proxy` (recommended) or `mcp-remote`, refer to the Codex MCP
  guide (`docs/codex-workspace-guide.md` or
  `resource://quick-memory/codex-workspace`).
- To scope an agent to a single project, issue a project-limited API
  key (via Users + Project Permissions) and use separate Codex server
  blocks per project as described in that guide.
- When calling `upsertEntry`, leave `id` empty to let the server generate a stable `<project>:<guid>` identifier automatically.
- Use the Overview tab in the Admin Web UI to verify `/health`, copy the user-specific snippet, and ensure the listed endpoints match the ones you configured for Codex. This page also contains the current API key snippet so you can refresh `~/.codex/config.toml` or `QuickMemoryServer.toml` when rotating keys.

## Troubleshooting
### Admin Web UI (browser)
- **401 / unauthorized**: your Admin Web UI session is invalid/expired. Use **Log out**, refresh `/`, and re-enter the API key. If the key was rotated or removed, create a new key in Users and try again.
- **Missing projects/endpoints**: the API key is not permitted for those projects. Check **Users** and **Project Permissions** in the Admin Web UI.

### MCP clients (Codex / `mcp-proxy` / `mcp-remote`)
- **401 / unauthorized from MCP tools**: the MCP client is sending an invalid API key. Update the `X-Api-Key` value in `~/.codex/config.toml` (or the env var your bridge reads) and restart Codex so the bridge reloads configuration. If you use `mcp-remote` and it keeps using an old key, delete `~/.mcp-auth/...` (or `%USERPROFILE%\\.mcp-auth\\...` on Windows) and restart.
- **“Endpoint not found” in Codex**: the API key is not scoped to that project. Confirm the user’s tier in **Project Permissions** and re-run `listProjects`.
- **“Transport closed” / timeouts**: retry once; if it repeats, call `GET /health` to confirm the server is up, then restart Codex to recreate the MCP connection.

### Service / installer
- **Installer warns about missing ONNX/JSONL files**: create placeholder `Models/` and `MemoryStores/` files or adjust `QuickMemoryServer.toml` paths to match your deployment layout.
- **Audit logs seem empty**: only permission edits write to `quick-memory-audit-*.log` / `.db`. Perform a change in **Project Permissions** (or bulk override) to generate entries, then query the SQLite DB.
