# Admin UI Help

Welcome to the Quick Memory Server admin console. This page collects quick tips for each blade.

## Overview
- Shows health status, counts, and per-store metadata.
- Links to `/metrics` and Help docs are on the page.

## Projects
- Add or edit projects (key, slug, storage, description, include/inherit shared flags).
- Save per-project metadata to memory (uses `projectMetadata` entries).
- Deleting a project removes its metadata and permissions but not the disk data; clean up storage manually if needed.

## Entities
- Select a project, refresh to browse recent entries (empty search uses recent list).
- Use search text for focused queries; include shared is on by default.
- View/Edit entries inline; delete honors permanence/tier rules.
- “Add entry” modal supports JSON bodies, relations, source metadata, and epic fields.

## Users & Project Permissions
- Manage API keys and default tiers per user.
- The Project Permissions panel lets you pick a project, review every user’s effective tier, and adjust overrides without editing raw JSON.
- Bulk override form applies a single user/tier change across every selected project; the UI warns if a project would lose its last Admin.
- Use project-scoped keys and overrides together to confine agent access.

## Config (TOML)
- Monaco editor with validation and diff preview.
- Validate before save; a diff shows changes vs. last loaded.
- Config writes to the service TOML and triggers a reload; keep a backup for safety.

## Health & Logs
- Fetch `/health` to see status, issues, and store info.
- Download recent log files as a zip; the service writes logs under `C:\Program Files\q-memory-mcp\logs`.

## Help
- End-user help: `/admin/help/end-user` (includes a Codex MCP quick start)
- Agent help: `/admin/help/agent` (links to the MemoryEntry field reference)
- MCP cheatsheet: `resource://quick-memory/cheatsheet`
- Codex MCP guide (global config + mcp-proxy examples): `/admin/help/codex-workspace` and `resource://quick-memory/codex-workspace`

Tips:
- If auth fails, re-enter your API key; sessions persist across refresh.
- Empty search in Entities shows recent entries; use tags/text for precision.
- For MCP clients, the base URL is `/mcp` with `X-Api-Key` header.
