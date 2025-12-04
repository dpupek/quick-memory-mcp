# Quick Memory Server MCP Usage (MCP-first)

## MCP Quickstart
- `listProjects` – discover allowed endpoints.
- `listRecentEntries` with `{ endpoint, maxResults }` – browse latest entries (no query needed).
- `searchEntries` with `{ endpoint, text, includeShared, maxResults }` – focused retrieval.
- `getEntry` / `listEntries` – fetch one or all entries in a project.
- `upsertEntry` / `patchEntry` / `deleteEntry` – mutate entries (permanent requires Admin; `entry.project` must match endpoint).
- `relatedEntries` with `{ id, maxHops }` – graph walk.
- `requestBackup` with `{ endpoint, mode }` (Admin) – queue backup.
- `health` – server health report.

## MCP Configuration (mcp-remote)
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
env = { AUTH_TOKEN = "<your-api-key>" }
```
Notes:
- Delete `~/.mcp-auth` after rotating keys to re-auth.
- Use project-scoped API keys to limit agent reach; disallowed projects are filtered/blocked.

## Common Payload Shapes
- **Relations:** array of `{ "type": "ref", "targetId": "project:key" }`.
- **Source metadata:** object `{ "type": "api", "url": "https://...", "path": "...", "shard": "..." }`.
- **Browse mode:** use `listRecentEntries` or call `searchEntries` with empty text to get recent entries.

## Recipes
### Browse then search
1) `listRecentEntries { endpoint: "projectA", maxResults: 20 }` to see latest items.
2) `searchEntries { endpoint: "projectA", text: "firewall rule" }` to narrow.

### Create or update an entry
```
upsertEntry {
  endpoint: "projectA",
  entry: {
    project: "projectA",
    id: "projectA:kb-firewall",
    kind: "procedure",
    title: "Update firewall rule",
    tags: ["firewall","ops"],
    curationTier: "curated",
    body: { text: "Steps..." },
    relations: [{ type:"ref", targetId:"projectA:net-baseline" }],
    source: { type:"api", url:"https://wiki" }
  }
}
```
Notes: project must equal endpoint; permanent entries require Admin.

### Patch an entry
`patchEntry { endpoint, id, title?, tags?, curationTier?, relations?, source? }`

### Related graph
`relatedEntries { endpoint:"projectA", id:"projectA:kb-firewall", maxHops:2 }` → nodes/edges.

### Backup
`requestBackup { endpoint:"projectA", mode:"differential" }` (Admin).

## Resources
- `resource://quick-memory/help` – main MCP help + quickstart.
- `resource://quick-memory/end-user-help` – end-user guide.
- `resource://quick-memory/cheatsheet` – condensed “do X → call Y”.

## Tips
- If auth fails, re-enter the API key; sessions persist in the SPA.
- Keys/slugs must match `^[A-Za-z0-9_-]+$`; storage defaults to `global.storageBasePath` + key if omitted.
- Use `listRecentEntries` for cold-start browsing; then `searchEntries` for precision.
