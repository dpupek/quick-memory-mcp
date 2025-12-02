# Quick Memory Server MCP Usage

## Getting Started
- Call `GET /admin/endpoints` (or the `describe` MCP command) first to learn about the available endpoints, commands, and required tiers.
- Authenticate every request with `X-Api-Key: <your key>`.
- Health check: `GET /health`.
- Connectivity test: `POST /mcp/<endpoint>/ping` with your API key.

## Admin SPA
- Visit `/` to open the embedded Bootstrap SPA; it locks the vertical menu behind the API key login overlay. Enter your key (no endpoint selection needed) and the UI will determine which projects you can access before showing Overview/Projects/Entities/Users tabs.
- **Overview** shows the `/health` report (status, uptime, store counts), the `/metrics` link, and a Codex configuration card that includes your API key snippet plus the endpoints you can reach, so you can copy that block into `QuickMemoryServer.toml`. **Projects** lists every configured endpoint you have access to and lets you save metadata by calling `upsertEntry` with `kind=projectMetadata`. **Entities** uses `searchEntries`/`patchEntry` for searching, browsing, and editing each entry from the existing MCP surface. **Users** relies on the new `/admin/users` and `/admin/permissions/{endpoint}` routes to mutate auth data, meaning you can CRUD API keys and adjust user tiers without restarting the service.
- SPA recipes: keep the login modal visible as long as your key is needed, refresh entity results with the "Refresh entries" button, and use the permissions editor to paste JSON payloads like `{ "alice": "Admin", "bob": "Curator" }` before saving.

## Memory Server Recipes

### Essentials
- `listEntries`: page/filter by `tags`, `kind`, `curationTier`, `updatedUtc`. Include shared results with `includeShared: true`.
- `searchEntries`: pass `text` and/or `embedding` (float array). Use `maxResults`, `threshold`, and `includeShared` to tune results.
- `relatedEntries`: supply `id`, `maxHops`, optional `relationTypes` for graph traversal.
- `getEntry`: fetch full entry by `id`.
- `summaries`: request cached/generated summaries for an `id` set.
- `backupStore`: queue differential (`mode=differential`, default) or full (`mode=full`) backup for the current endpoint (admin tier).

### Recipe: Detect project-wide changes
1. Track the last `updatedUtc` you saw for the project.
2. Call `POST /mcp/{project}/searchEntries` with `maxResults` large enough and `includeShared=false`.
3. Inspect each returned `entry.timestamps.updatedUtc`; if any timestamp is newer than your cached value, mark the project dirty and update your watermark.
4. Alternatively, use `GET /mcp/{project}/entries` and filter client-side (e.g., only top 100 sorted by `updatedUtc`).

### Recipe: Inspect a specific entry
1. Call `GET /mcp/{project}/entries/{entryId}` with your API key.
2. Verify `entry.timestamps.updatedUtc` vs. your cached copy; if they differ, use the returned body/metadata as your fresh version.
3. For embedded context (relations/snippets), rely on `searchEntries` with that `entryId` if you need the `scores` or shared references.

### Recipe: Rehydrate canonical knowledge
1. Run `listEntries` filtered on `curationTier=canonical` (or `listCurated` if provided).
2. Cache the returned entries plus `updatedUtc`/`relations` so you can respond quickly.
3. Re-run periodically and compare timestamps to know when canonical knowledge shifts.

### Recipe: Traverse shared context
1. Call `relatedEntries` with `maxHops=3` (or more if needed) and `includeShared=true`.
2. Use the returned node IDs and `getEntry` to fetch full bodies, keeping shared/contextual memories in sync.

### Recipe: Audit before mutation
1. `GET /mcp/{project}/entries/{id}` to capture `curationTier`, `isPermanent`, and `confidence`.
2. If you plan to flip canonical/permanent flags, ensure your tier permits it and log the before/after values.
3. Apply `patchEntry` with the deltas and re-fetch to confirm the change.

### Recipe: Summarize conversation threads
1. Collect IDs of `kind=conversationTurn` via `searchEntries`.
2. Call `summaries` with those IDs; the service will generate/cached summaries.
3. Use the cached summary until any referenced entry’s `updatedUtc` moves forward.


### Recipe: Trigger backups

1. POST to `/mcp/{project}/backup` with `{ "mode": "full" }` or `{ "mode": "differential" }` (admin only).
2. The response indicates the queued mode; monitor `Backups/` for the snapshot files.
3. To restore, stop the service, swap in the backup folder, and restart—the watcher reloads automatically.

### Recipe: Capture a decision with provenance
1. Log the unresolved issue as a `question` entry (include `context`, `relatedEntries`).
2. Once resolved, upsert a `decision` entry linking to `questionId`, including `decision`, `rationale`, and `owner`. Mark it `curationTier=canonical`.
3. Use `searchEntries` plus `relatedEntries` to fetch the question/decision pair later.

Decision example:
```json
{
  "kind": "decision",
  "project": "projectA",
  "questionId": "projectA:Q-2025",
  "decision": "Enable feature gradually",
  "rationale": "Metrics look stable",
  "owner": "alice",
  "curationTier": "canonical"
}
```

### Recipe: Record an observation
1. When you capture a metric spike, create an `observation` entry with `metric`, `value`, `unit`, and `source`.
2. Tag it (e.g., `["alert", "backend"]`) and link to related facts via `relations`.
3. Search `kind=observation` entries to review trends.

Observation example:
```json
{
  "kind": "observation",
  "project": "projectA",
  "metric": "error-rate",
  "value": 0.05,
  "unit": "pct",
  "period": "2025-11-04T12:00:00Z",
  "source": "tracing",
  "tags": ["alert", "backend"]
}
```

### Recipe: Manage tasks
1. Emit a `task` entry (`summary`, `status`, `assignee`, `dueUtc`) whenever follow-up is required.
2. Update its status via `patchEntry` and link to context entries.
3. Filter by `kind=task` to build your to-do list.

Task example:
```json
{
  "kind": "task",
  "project": "projectA",
  "summary": "Document backup choreography",
  "status": "open",
  "assignee": "bob",
  "dueUtc": "2025-11-20T00:00:00Z",
  "tags": ["ops", "docs"]
}
```

### Recipe: Question-driven follow-ups
1. Capture the question with `kind=question` and `context`.
2. Later, record the linked `decision` entry.
3. Use `relatedEntries`/`searchEntries` to retrieve both when revisiting the topic.

### Recipe: Observation-triggered actions
1. When an `observation` crosses a threshold, spawn a `task` or `decision` entry linked via `relations`.
2. This ties the alert, resolution, and follow-up together for future agents.

## Tool metadata & how-to recipes
- The runtime JSON Schema for `MemoryEntry` and `SearchRequest` lives under `GET /docs/schema` (ETag + Cache‑Control). Include that URL in agent bootstrap flows so you can validate payloads before you call tools.
- Every tool advertised in the `describe` payload now includes `meta.description`, `meta.tier`, and `meta.recipe` (plus an optional `meta.helpUrl`) thanks to the `McpMeta` attributes on each handler. Call `describe` once to pull those details, then check the listed `recipe` string for the quick “how-to” that matches the recipes documented above.
- Use `meta.tier` to know if the caller’s permissions allow invoking `upsertEntry`/`deleteEntry`/`requestBackup` (the SPA propagates that tier into the login state so Learn/Agents can respect the guardrails).
- The dedicated recipe sections above (searchEntries, relatedEntries, backup, etc.) walk through the typical payloads, while the `meta.description` text is a concise summary of each tool’s behavior; keep both in sync if the tool parameters change.
## Curation Tier Matrix
| Tier | Description | Allowed operations | Notes |
|------|-------------|--------------------|-------|
| `reader` | Read-only recall and discovery. | `searchEntries`, `listEntries`, `getEntry`, `relatedEntries`, `summaries`, `describe`, `getUsageDoc`. | Default tier; cannot mutate entries/backups. |
| `editor` | Provisional entry author. | Reader commands plus `upsertEntry`, `bulkImport`, `patchEntry` for non-canonical fields. | Still barred from setting `curationTier=canonical` or touching `isPermanent`. |
| `curator` | Promotes curated knowledge. | Editor privileges plus `patchEntry` to `curated`/`canonical`, `deleteEntry` when `!isPermanent`, and `backupStore` requests. | Requires `force` to remove permanent entries. |
| `admin` | Full control. | All commands, including `deleteEntry` with `force`, `backupStore`, and editing `isPermanent` entries. | Limit to operators; rotate API keys when needed. |

## Mutating Commands
- `upsertEntry`: supply full entry payload (see schema below). `curationTier` changes require curator/admin tier.
- `patchEntry`: targeted updates (tags, tier, pinned, confidence). Set `force: true` to override permanence as admin.
- `deleteEntry`: soft delete by default. `force` allows permanent removal when `isPermanent == false`.
- `bulkImport`: array of new entries; response includes per-item status.

## Schema Highlights
- `kind` supports `note`, `fact`, `procedure`, `conversationTurn`, `timelineEvent`, `codeSnippet`, `decision`, `observation`, `question`, `task`.
- `decision` entries link back to the driving `questionId` and capture rationale/ownership; use them to seal answers.
- `curationTier`: `canonical` > `curated` > `provisional`.
- `isPermanent: true` blocks deletion and TTL pruning.
- `embedding`: float array length defined by config (`global.embeddingDims`).
- `relations`: objects with `type`, `targetId`, optional `weight`.
- Optional `epicSlug`/`epicCase` fields let you scope entries to a specific workstream or ticket without abusing tags.

## Examples
```json
{
  "command": "searchEntries",
  "project": "projectA",
  "query": {
    "text": "vector index rebuild",
    "maxResults": 10,
    "includeShared": true
  }
}
```

```json
{
  "command": "upsertEntry",
  "entry": {
    "id": "projectA:release-notes",
    "project": "projectA",
    "kind": "note",
    "title": "Release 1.2 summary",
    "body": { "text": "..." },
    "tags": ["release", "2024"],
    "curationTier": "curated"
  }
}
```

## Best Practices
- Call `describe` periodically to refresh capabilities.
- Cache responses locally; avoid requesting unchanged entries.
- Use `listCurated` to bootstrap canonical knowledge.
- Trigger `describe` or `getUsageDoc` whenever the server version changes.
- For large writes, prefer `bulkImport` and monitor the response for failures.
- Command-line helper: `memoryctl backup <endpoint> --mode full --api-key ... --url http://host:5080` queues backups from scripts.
