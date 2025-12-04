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

## MemoryEntry field reference

| Field | Description |
|-------|-------------|
| `schemaVersion` | Positive integer for versioning the entry format (current value `1`). Reserved for future migrations; agents normally leave this alone. |
| `id` | Stable identifier in the form `project:key`. Used as the primary key in JSONL, search results, relations, and MCP calls. If omitted during `upsertEntry`, the server generates `<project>:<guid>`, then you can use that ID for future updates/links. |
| `project` | Logical store the entry belongs to (e.g., `projectA`). Must match the endpoint/key you call; the router uses it to pick the correct `MemoryStore` and to filter cross-project results. Defaults to the endpoint if left blank. |
| `kind` | Category of memory (`note`, `fact`, `procedure`, `conversationTurn`, `timelineEvent`, `codeSnippet`, `decision`, `observation`, `question`, `task`, etc.). Used to drive UI hints and query semantics (e.g., agents can ask “only tasks” or “only timeline events”). |
| `title` | Short human-readable label. Shown in SPA tables, search results, and graph visualizations; if empty the UI falls back to `id`, which is much harder to scan. |
| `body` | The actual content of the memory. Can be plain text or a JSON object. When JSON, try to keep a consistent shape per `kind` (`steps` for procedures, `snippet` for code, etc.) so agents can parse/augment it safely. Changes to `body` drive embedding recomputation and can change search ranking. |
| `tags` | Free-form labels used for faceted search (“only entries tagged `backup`”). The search engine indexes these separately, and the SPA tags editor exposes them directly. Use them for coarse-grained grouping across kinds (`ops`, `design`, `decision`). |
| `keywords` | Optional, usually system- or tool-generated keywords (e.g., extracted phrases). They don’t drive any special logic today beyond search, so agents can ignore or populate them as needed. |
| `relations` | Array of `{ type, targetId, weight? }` describing graph edges to other entries. `targetId` must be a valid `project:key`. `relatedEntries` and some UI flows use this to hop the knowledge graph (e.g., “see-also”, “dependency”). Use it when you want agents to follow curated connections instead of guessing links from text. |
| `source` | `{ type, url, path, shard }` metadata pointing back to where this memory came from: HTTP API, local file, shard ID, etc. Useful for audit/troubleshooting (e.g., “regenerate from this path”) and for agents deciding whether a memory is authoritative or derived. |
| `embedding` | Semantic vector backing hybrid search. If omitted, the server computes it from `body`. You only care about this field if you’re doing offline imports or using your own embedding model; otherwise, let the server manage it. Length must match `global.embeddingDims`. |
| `timestamps` | `{ createdUtc, updatedUtc, sourceUtc? }` used for recency bias and `listRecentEntries`. If unset, the server fills `createdUtc` / `updatedUtc` with “now”. Agents can use these fields to prioritize fresh memories or filter out stale ones. |
| `ttlUtc` | Optional expiration time (“soft delete” after a given instant). The loader can ignore entries past TTL, which is useful for temporary experiments or noisy telemetry. This is ignored when `isPermanent = true`. |
| `confidence` | A 0–1 score representing how trustworthy the memory is (e.g., from your ingestion pipeline). Higher confidence can bias ranking and help agents choose between conflicting memories. Defaults to `0.5` if omitted; agents may use it to down-rank low-confidence facts instead of deleting them. |
| `curationTier` | Editorial status: `provisional` (default, unreviewed), `curated` (reviewed and solid), or `canonical` (the source of truth). The server enforces the enum and uses it in ranking; UI badges and some workflows highlight canonical entries. Admin tier is required to make permanent entries canonical. |
| `epicSlug` / `epicCase` | Optional linkage to your planning system (epics, FogBugz/Jira cases, etc.). Use these when you want agents to reason over “everything attached to epic X” without relying purely on tags. |
| `isPermanent` | Hard guardrail that prevents deletion unless `force = true` and the caller is Admin. Use it for decisions, contracts, or safety-critical facts you never want casually removed. Defaults to `false`. |
| `pinned` | Soft flag that UIs and agents can use to emphasize important entries in lists or summaries (e.g., “show pinned items first”). Does not change search semantics by itself but is intended for UX/agent sorting hints. |

**Notes**
- `upsertEntry` enforces `project` equality and fills `id`, `timestamps`, and `curationTier` defaults automatically.
- `patchEntry` accepts the same fields but only replaces those present; relations/source payloads must remain valid JSON objects/arrays.
- Permanent entries can only be deleted with `deleteEntry(..., force: true)` by Admin-tier keys.

## Resources
- `resource://quick-memory/help` – main MCP help + quickstart.
- `resource://quick-memory/end-user-help` – end-user guide.
- `resource://quick-memory/cheatsheet` – condensed “do X → call Y”.
- `resource://quick-memory/entry-fields` – MemoryEntry field reference (IDs, kinds, body, tags, relations, source, timestamps, TTL, tiers). The same table appears in `/admin/help/agent`.

## Tips
- If auth fails, re-enter the API key; sessions persist in the SPA.
- Keys/slugs must match `^[A-Za-z0-9_-]+$`; storage defaults to `global.storageBasePath` + key if omitted.
- Use `listRecentEntries` for cold-start browsing; then `searchEntries` for precision.
