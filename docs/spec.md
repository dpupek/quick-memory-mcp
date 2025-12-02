# Quick Memory Server Specification

## Overview
- **Purpose**: Provide a Windows-service-hosted MCP memory server offering isolated per-project memories plus shared/global memory.
- **Consumers**: AI agents speaking MCP, administrative tooling, and manual editors working directly with JSONL files.
- **Non-Goals**: Distributed clustering, remote storage beyond local disk, or user-facing GUI.

## Runtime Requirements
- .NET 9 Worker Service using `Host.CreateApplicationBuilder` and `UseWindowsService`.
- Hosts Kestrel with multiple MCP endpoints (`/mcp/shared`, `/mcp/{project}`) derived from configuration.
- Operates in RAM-first mode: loads entries & indexes into memory, incrementally refreshes on file changes.
- Monitors disk files with `FileSystemWatcher` (one per store) with debounce & retry logic.
- Supports graceful shutdown (flush pending writes), startup preloading, and Event Log logging.
- Uses dense vector embeddings (e.g., 1,536-d float arrays) to capture semantic meaning of each entry for hybrid search. Embeddings are produced by a local model pipeline and refreshed when `body` text/snippets change.
 - Uses dense vector embeddings (e.g., 1,536-d float arrays) to capture semantic meaning of each entry for hybrid search. Embeddings are produced by a local ONNX pipeline (or deterministic hash fallback when the model is unavailable) and refreshed when `body` text/snippets change.

## Configuration & Authentication
- Configuration stored in an INI/TOML file located alongside the service executable (e.g., `QuickMemoryServer.toml`).
- File structure:
  - `[global]`: service name, listening URLs, embedding model settings, backup cadence.
  - `[endpoint.projectA]`: `slug`, `name`, `description`, `storagePath`, optional shared inheritance flags.
  - `[users.alice]`: `apiKey`, `tier` (e.g., `admin`, `curator`, `reader`).
  - `[permissions.projectA]`: per-user tier overrides; multiple users can point at multiple endpoints.
- Authentication: clients present API key via header; router resolves user tier, applies endpoint-specific ACLs.
- Permission tiers (configurable but expected defaults):
  - `admin`: full access, can modify canonical/permanent entries and manage backups.
  - `curator`: can mark entries curated/canonical but cannot delete permanent entries without override.
  - `reader`: read/search only.
- ONNX model artifacts for embeddings and summaries are deployed with the installer under `Models/embeddings.onnx` and `Models/summarizer.onnx`; config references them via relative paths to support portable installs.

## Component Architecture
- **MemoryService** (`BackgroundService`): orchestrates preload, scheduled flushes, and exposes health state.
- **MemoryRouter**: resolves MCP endpoint/project to the appropriate `MemoryStore` and shared context.
- **MemoryStore**: manages per-project JSONL files, indexes, cross references, in-memory cache, and concurrency locks.
- **SharedMemoryStore**: same contract as `MemoryStore`; can be injected into other stores for shared lookups.
- **FileWatcher**: wraps `FileSystemWatcher`, debounces events, and publishes reload notifications.
- **SearchEngine**: coordinates Lucene keyword index plus vector index; merges cross-store results.
- **GraphIndex**: maintains adjacency lists from `relations` for cross-reference queries.
- **Persistence**: `JsonlRepository` handles framed reads/writes with versioning and atomic replacements.
- **MCP Adapter**: ASP.NET Core minimal APIs translating MCP requests to domain commands; handles auth/quotas.
 - **Diagnostics**: Serilog/EventLog, EventCounters for memory pressure, and optional Prometheus endpoint.

## Schema & Help Docs
- Provide the runtime JSON Schema via `GET /docs/schema` (includes the `MemoryEntry` and `SearchRequest` structures and a generated timestamp), cache it with `ETag`/`Cache-Control`, and expose the URL so agents can validate payloads.
- Surface `/admin/help/agent` and `/admin/help/end-user` in the MCP `describe` response (via `ServerInstructions`) so clients know where to read the recipes/help before calling tools.

## Storage Layout
```
MemoryStores/
  shared/
    entries.jsonl
    indexes/
      lucene/
      vectors.dat
      graph.json
  projectA/
    entries.jsonl
    indexes/...
```
- JSONL keeps entries human editable and diff-friendly.
- Indexes persist to disk to speed restarts; rebuilding from RAM is supported.
- Backups stored under `Backups/{timestamp}/<store>/entries.jsonl`.

## Installer Layout
- Single install root (default `C:\Program Files\QuickMemoryServer\`):
  - `QuickMemoryServer.exe` — published single-file worker.
  - `QuickMemoryServer.toml` — primary configuration; installer seeds default endpoints/users.
  - `Models\embeddings.onnx` — embedding model shipped with hash manifest.
  - `Models\summarizer.onnx` — summarization model.
  - `MemoryStores\` — initial directory tree with `shared\entries.jsonl` placeholder; per-project folders created post-install.
  - `logs\` — EventLog forwarder/rolling file sink (optional).
  - `tools\memoryctl.exe` — administrative CLI for rebuild/backups.
  - `docs\` — README, schema reference, change log.
- Installer writes a `layout.json` manifest enumerating files, SHA-256 hashes, and writable directories.
- Service account granted modify rights on `MemoryStores`, `logs`, `Backups`; executables/config remain read-only.
- Upgrades reuse manifest to validate existing files and replace models/config templates as needed.

## Memory Entry Schema
### Common Fields
```json
{
  "schemaVersion": 1,
  "id": "projA:7f3c1c32-9a02-4a5f-a59e-5952b4202f23",
  "project": "projA",
  "kind": "fact",
  "title": "Search index rebuild guard",
  "body": { "statement": "..." },
  "tags": ["search", "indexing"],
  "source": { "type": "doc", "path": "docs/spec.md" },
  "embedding": [0.123, -0.044, "..."],
  "keywords": ["lucene", "reload guard"],
  "relations": [
    { "type": "ref", "targetId": "shared:abc123", "weight": 0.7 }
  ],
  "timestamps": {
    "createdUtc": "2024-05-12T18:03:54Z",
    "updatedUtc": "2024-05-12T19:11:22Z",
    "sourceUtc": "2024-05-12T17:59:00Z"
  },
  "ttlUtc": null,
  "confidence": 0.92,
  "curationTier": "canonical",
  "isPermanent": true,
  "pinned": false
}
```

### `body` Variants
- `note`: `{ "text": "...", "context": "..." }`
- `fact`: `{ "statement": "...", "justification": "...", "metrics": [{ "name": "...", "value": 42, "unit": "ms" }] }`
- `procedure`: `{ "steps": ["..."], "prerequisites": ["..."] }`
- `conversationTurn`: `{ "role": "assistant", "content": "...", "conversationId": "...." }`
- `timelineEvent`: `{ "occurredUtc": "...", "summary": "...", "impact": "...", "links": ["..."] }`
- `codeSnippet`: `{ "language": "csharp", "snippet": "public record ...", "context": "...", "explanation": "...", "files": [{ "path": "src/MemoryStore.cs", "lineStart": 42, "lineEnd": 68 }], "executionStatus": "tested", "lastRunUtc": "..." }`
- `decision`: `{ "questionId": "project:123", "decision": "Approve rollout", "rationale": "Metrics look good", "owner": "alice" }`
- `observation`: `{ "metric": "error-rate", "value": 0.02, "unit": "pct", "period": "2025-11-02", "source": "tracing" }`
- `question`: `{ "text": "Should we deploy?", "context": "QA is green", "relatedEntries": ["project:101"] }`
- `task`: `{ "summary": "Update docs", "status": "open", "assignee": "bob", "dueUtc": "2025-11-20T00:00:00Z" }`
- Optional fields `epicSlug` and `epicCase`—use them to tag entries with the owning epic or case number, making filters first class without relying solely on tags.

### Semantics
- Missing `curationTier` defaults to `provisional`; `isPermanent` defaults to `false`.
- `embedding` length is tied to the configured model; loader validates and rejects mismatches.
- `relations` referencing other stores use canonical IDs (`project:id`).
- `ttlUtc` ignored if `isPermanent` is true.
- Embeddings are produced locally through a configurable model (e.g., ONNX) defined in the `[global]` section; CLI tools can regenerate vectors when the model changes.
- Installer bundles default ONNX models; runtime verifies hashes during startup to guard against tampering.

## Summaries
- `summaries` command returns concise representations of one or more entries.
- Precomputed summaries live under `indexes/summaries.json` with `{ "id": "...", "summary": "..." }` objects.
- If a summary is missing, the server can generate it on demand using the configured summarizer pipeline, then cache it in memory and persist during the next flush.
- Summary generation respects permissions: only tiers allowed to read `body` content can request generation.
- Summarizer ONNX model ships alongside the executable and is referenced in config to avoid runtime downloads.

## MCP Command Surface
| Command | Description | Request Payload | Response Payload | Notes |
|---------|-------------|-----------------|------------------|-------|
| `listEntries` | Enumerate entries for a store (paged/filterable). | `{ "project": "projA", "filter": { "tags": [], "tier": "curated" }, "page": { "size": 50, "cursor": null } }` | `{ "items": [MemoryEntrySummary], "nextCursor": null }` | Supports filtering by tags, `curationTier`, `kind`, time range. |
| `getEntry` | Fetch full entry by ID. | `{ "id": "projA:7f3c..." }` | `{ "entry": MemoryEntry }` | Returns 404 if missing. |
| `upsertEntry` | Create or update entry (full replacement). | `{ "entry": MemoryEntry }` | `{ "version": "etag", "updated": true }` | Validates schema version; canonical updates logged. |
| `patchEntry` | Partial update (for curated/permanent toggles). | `{ "id": "...", "patch": { "curationTier": "canonical" } }` | `{ "entry": MemoryEntry }` | Enforces tier enum & bool toggles. |
| `deleteEntry` | Remove entry (unless permanent). | `{ "id": "...", "force": false }` | `{ "deleted": true }` | Rejects if `isPermanent` true and `force` false. |
| `searchEntries` | Hybrid keyword/vector search. | `{ "project": "projA", "query": { "text": "...", "embedding": [..], "maxResults": 25, "includeShared": true } }` | `{ "results": [ScoredEntry] }` | Re-ranks with tier/confidence. |
| `relatedEntries` | Graph traversal for cross references. | `{ "id": "...", "maxHops": 2, "project": "projA" }` | `{ "nodes": [...], "edges": [...] }` | Optionally include shared store neighbors. |
| `summaries` | Return precomputed or generated summaries. | `{ "project": "projA", "ids": ["..."] }` | `{ "summaries": [{ "id": "...", "summary": "..." }] }` | Uses cached `summaries.json` when available. |
| `listCurated` | List canonical/curated entries. | `{ "project": "projA", "tier": "canonical" }` | `{ "items": [MemoryEntrySummary] }` | Shortcut building on `listEntries`. |
| `bulkImport` | Append multiple entries at once. | `{ "project": "projA", "entries": [MemoryEntry] }` | `{ "imported": 5, "skipped": 1 }` | Batches writes for atomicity. |
| `health` | Report store/server status. | `{}` | `{ "status": "Healthy", "stores": [{ "name": "...", "entries": 1200, "loaded": true }] }` | Used by supervisors. |
| `describe` | Return catalog of supported commands, required auth tiers, sample payloads. | `{}` | `{ "commands": [ { "name": "searchEntries", "tier": "reader", "requestSchema": {"text": "string"} } ], "kinds": [...], "docUrl": "/docs/agent-usage" }` | First-call hint for autonomous agents. |
| `getUsageDoc` | Provide Markdown onboarding for agents. | `{ "format": "markdown" }` | `{ "content": "# Quick Memory Server..." }` | Mirrors `docs/agent-usage.md`; cache client-side. |
| `backupStore` | Queue a differential/full backup for a store. | `{ "mode": "full" }` | `{ "queued": true, "mode": "Full" }` | Admin tier only; routes through `BackupService`. |

## CRC Tables
### Core Entities
| Class | Responsibilities | Collaborators |
|-------|------------------|---------------|
| `MemoryEntry` | Value object representing a memory; enforces schema validation & defaults. | `MemoryStore`, `SearchEngine`, `GraphIndex`. |
| `SearchQuery` | Encapsulates search parameters, including text/embedding filters. | `MemoryRouter`, `SearchEngine`. |
| `SearchResult` | Holds scored entries and metadata for responses. | `SearchEngine`, MCP adapters. |
| `RelationEdge` | Represents a graph edge between entries with weight/type. | `GraphIndex`, `MemoryEntry`. |

### Services & Managers
| Class | Responsibilities | Collaborators |
|-------|------------------|---------------|
| `MemoryService` | Lifecycle management: preload stores, schedule flushes, emit health. | `MemoryStoreFactory`, `SearchEngine`, `ILogger`, `TimeProvider`. |
| `MemoryStore` | Manage per-project data, caches, file IO, and index updates. | `JsonlRepository`, `FileWatcher`, `SearchEngine`, `GraphIndex`. |
| `SharedMemoryStore` | Shared/global store with same contract and cross-store hooks. | `MemoryStore`, `SearchEngine`, `GraphIndex`. |
| `MemoryStoreFactory` | Build/lookup stores from configuration; preload and flush them. | `IOptions<MemoryStoreOptions>`, `SearchEngine`, `IMemoryCache`. |
| `MemoryRouter` | Resolve MCP endpoints to stores and enforce access rules. | `MemoryStoreFactory`, MCP adapter. |
| `FileWatcher` | Observe file changes, debounce events, push reload notifications. | `MemoryStore`, `ILogger`. |
| `JsonlRepository` | Read/write JSONL entries with buffering, migrations, and atomic swaps. | `MemoryStore`, `IOptions`. |
| `SearchEngine` | Maintain Lucene/vector indexes; execute searches and ranking. | `LuceneStore`, `VectorIndex`, `MemoryStore`. |
| `LuceneStore` | Encapsulate Lucene directory/writer/reader lifecycle per project. | `SearchEngine`. |
| `VectorIndex` | In-memory vector storage/search; persist to disk when updated. | `SearchEngine`, `MemoryStore`. |
| `GraphIndex` | Build relation graph for cross references and path queries. | `MemoryStore`, `MemoryEntry`. |
| `McpRequestHandler` | Translate MCP JSON RPC to domain commands; handle auth and response shaping. | `MemoryRouter`, `SearchEngine`, `MemoryStore`. |
| `BackupService` | Periodic snapshotting of entries/indexes. | `MemoryStoreFactory`, file system. |
| `HealthReporter` | Aggregate runtime metrics for `health` command & logging. | `MemoryStoreFactory`, `EventCounters`. |

## Administrative Workflows
1. **Manual Editing**: Update `entries.jsonl`, save; watchers reload stores, rebuild indexes, and log outcomes.
2. **Administrative UI**: Backup scheduling, permissions, and endpoint metadata remain exposed through the SPA’s Projects/Users blades and their HTTP helper APIs rather than as MCP tools.
3. **MCP Mutations**: Use `upsertEntry`/`patchEntry` to manage entity metadata; canonical/permanent guards enforce consistency.
4. **Deployment**: Publish single-file `win-x64` executable; register via `sc create QuickMemoryServer binPath= "..." start= auto`.

## Admin Console & Configuration APIs
- **SPA Shell**: The `/` route now serves a Bootstrap 5 vertical menu SPA that surfaces Overview/Projects/Entities/Users tabs, requires an API key login, and proxies every mutation through the existing MCP surface. The SPA displays `/metrics` links, `/health` snapshots, project metadata, entity search/edit flows, and user/permission management panels without needing an additional backend stack.
- **Admin APIs**: Behind the SPA there are two lightweight helpers: `GET/POST/DELETE /admin/users` (manage user records and tiers) and `GET/POST /admin/permissions/{endpoint}` (assign endpoint-specific tiers). They persist to `QuickMemoryServer.toml` via `AdminConfigService`, trigger a configuration reload, and respect tier authorization (Admin only).

## Non-Functional Considerations
- **Performance**: Aim for <100 ms median search using cached indexes; async file IO; limit Lucene writer contention via background queue.
- **Reliability**: Watchers retry on transient IO errors; manual reload command available; canonical entries guard rails ensure reproducibility.
- **Extensibility**: Schema versioning plus `body` polymorphism allow future kinds; configuration-driven endpoints accommodate new projects.
- **Security**: API key–based authentication with per-user tiers; Windows ACLs on storage folders; logging redacts sensitive `body` fields if flagged.
- **Observability**: Structured Serilog console/file logs, EventLog integration, and Prometheus `/metrics` (via `prometheus-net`) tracking MCP requests (`qms_mcp_requests_total`, `qms_mcp_request_duration_seconds`) and per-store cache sizes (`qms_store_entry_count`). EventCounter telemetry (`QuickMemoryServer.Observability`) surfaces request rates and managed memory, while the `/health` endpoint now returns the `HealthReport` payload generated by `HealthReporter`.

## Administrative Workflows
1. **Manual Editing**: Update `entries.jsonl`, save; watcher reloads store, rebuilds indexes, logs success/failure.
2. **MCP Mutations**: Use `upsertEntry`/`patchEntry` to mark `curationTier` or `isPermanent`; canonical changes recorded in audit log.
3. **Backups**: Trigger `backupStore` (MCP) or run CLI `memoryctl backup projectA --mode full`; choose `differential` (default) or `full`.
4. **Deployment**: Publish single-file `win-x64` executable; register via `sc create QuickMemoryServer binPath= "..." start= auto`.
