# Quick Memory Server Implementation Plan

## Completed Phases
- [x] **Phase 0 – Foundations**
  - **Tasks**: Create repo structure, add the solution/projects, configure .NET 9 global.json, enable nullable/context, and draft the installer layout manifest (`layout.json`).
  - **Deliverables**: `QuickMemoryServer.sln`, a minimal worker in `src/QuickMemoryServer.Worker`, spec-aware README, and manifest-driven layout.
  - **Acceptance**: Builds locally, service runs under console/Windows service, and layout manifest approved.
- [x] **Phase 1 – Configuration & Hosting**
  - **Tasks**: Parse TOML/INI configuration beside the executable, wire `UseWindowsService`, Kestrel, MCP routing stubs, and structured logging via Serilog/EventLog.
  - **Deliverables**: Running service with stubbed MCP endpoints per store and API-key auth.
  - **Acceptance**: Console + Windows service start-up succeed and endpoints respond with placeholder payloads.
- [x] **Phase 2 – Persistence Layer**
  - **Tasks**: Implement `JsonlRepository` with streaming reads/writes, schema validation, and atomic flushes; create `MemoryEntry` model honoring tier/permanent defaults and embedding shape.
  - **Deliverables**: Disk-backed stores loading entries into memory safely.
  - **Acceptance**: CRUD tests pass and JSONL edits trigger reloads.
- [x] **Phase 3 – Store & Watcher Infrastructure**
  - **Tasks**: Build `MemoryStore`, `SharedMemoryStore`, factory, `FileWatcher`, and `MemoryService` background loop with caching.
  - **Deliverables**: RAM-first caches synchronized with disk and reload-on-change logic.
  - **Acceptance**: Watcher/integration tests ensure reloads/concurrency.
- [x] **Phase 4 – Search & Graph**
  - **Tasks**: Implement `SearchEngine` (Lucene + vector), `GraphIndex`, ONNX embedding/summarization pipelines, and ranking influenced by tiers/permanence.
  - **Deliverables**: Search + related entries flows, ONNX pipeline artifacts, and CLI rebuild helpers.
  - **Acceptance**: Benchmarks meet latency targets; embeddings and summaries produced/cached.
- [x] **Phase 5 – MCP Command Surface**
  - **Tasks**: Wire MCP adapter for search, related, CRUD, project helpers, help blades, and a health report; reuse `MemoryRouter` for access control.
  - **Deliverables**: Tool definitions (`searchEntries`, `relatedEntries`, `listEntries`, `upsertEntry`, `patchEntry`, `deleteEntry`, etc.) with canonical/permanent enforcement.
  - **Acceptance**: MCP contract tests pass, canonical/permanent guard rails enforced, `describe`/`getUsageDoc` documents commands.
- [x] **Phase 6 – Tooling & Ops**
  - **Tasks**: Harden backups/reloads via admin UI helpers, add EventCounters/Prometheus metrics covering MCP requests, and keep installer/scripts synchronized with ONNX tooling.
  - **Deliverables**: Admin workflows accessible via HTTP, `/metrics` + `/health` endpoints, documented installation artifacts.
  - **Acceptance**: Admin operations function via HTTP, telemetry surfaces, deployment instructions updated.
- [x] **Phase 7 – Observability Baseline**
  - **Tasks**: Instrument MCP surfaces/backups with Prometheus counters, EventCounters, Serilog traces, and a `HealthReport` endpoint; document the observability surface.
  - **Deliverables**: Prometheus metrics (`qms_*`), EventCounters, `/health` response, and updated doc coverage.
  - **Acceptance**: Metrics cover requests/backups/entry counts; `/health` returns a structured report.

-## Next Phase (Phase 8) – Streamable MCP Server Implementation
- [x] **Task 8.1:** Integrate the Model Context Protocol C# SDK (`ModelContextProtocol` NuGet), host `MapMcp` Streamable HTTP endpoints, and reuse existing services for each MCP tool/tool description.
- [x] **Task 8.2:** Ensure the streamable transport exposes tools for `searchEntries`, `relatedEntries`, entry/project CRUD, help blades, health, backups, and admin info; reuse `MemoryRouter` + permission tiers.
- [x] **Task 8.3:** Extend `/mcp/describe` payload to include the runtime schema URL and the latest `agent-usage`/help doc references from `/docs/agent-usage.md`.
- [x] **Task 8.4:** Cache the generated JSON schema, surface it at `/docs/schema`, emit ETag/Cache-Control headers, and teach MCP clients (via `describe`) where to find it.
- [x] **Task 8.5:** Update `docs/spec.md`, `docs/agent-usage.md`, and the README with Streamable transport details, tool listings, and configuration samples (TOML + `.codex/config.toml`).
- [x] **Task 8.6:** Document MCP command recipes (search, entry checks, project changes) within `docs/agent-usage.md`, including payload examples, canonical/permanent behaviors, and curation tier matrix.

## Phase 9 – Embedded Admin SPA
- [x] Reload blade content from the server whenever switching tabs (Overview/Projects/Entities/Users/Help/Health) via per-tab view controllers.
- [x] Ensure the Projects list refreshes immediately after creating, updating, or deleting a project.
- [x] Persist login across page refreshes using the server-side session-backed `/admin/login` + `/admin/session` flow.
- [x] Add a Config blade for viewing/editing the raw TOML configuration with validation and a safe-apply flow.
- [x] Add a Health blade that surfaces `/health` details and exposes one-click log download from the server.
- [ ] Add MCP usage cheatsheet resource (`resource://quick-memory/cheatsheet`) with concise “do X → call Y” guidance.
- [x] Add MCP `listRecentEntries`-style browse behavior to the Entities tab when no query is provided.
- [x] UI polish: integrate Bootstrap Icons for nav/buttons and SweetAlert2 for confirmations/toasts; enhance tags input with Choices.js.
- [x] Rename “Endpoint permissions” to “Project permissions” across nav labels, API payloads, and docs to match user vocabulary.
- [x] Build a dedicated Project-permissions editor: left pane lists projects, right pane shows per-user overrides with dropdown tiers, add/remove user controls, and inherit-shared indicator.
- [x] Support bulk updates (apply the same tier override to multiple projects) and warn when a project has no Admin tier after edits.
- [x] Expose GET/PATCH endpoints (`/admin/projects/{key}/permissions`) so the SPA doesn’t rewrite the full TOML on each change.
- [x] Surface an audit trail of permission changes (who/when/what) for future troubleshooting.
- [x] Move the SPA shell into a Razor view (`Views/Admin/Index.cshtml`) and enable cache-busted static assets via `asp-append-version` for `app.css` and `app.js`.
- [x] Auto-create the `prompts-repository` project at startup when missing so curated prompt entries are always reachable from the SPA and MCP tools.
- [x] Add a delete button next to each entry row in the Entities table for quick single-entry deletion.
- [x] Add entry selection checkboxes plus bulk delete actions in the Entities tab (with confirmation and permission checks).

## Future Phases
- [ ] **Phase 10 – Release & Hardening** (installer packaging with WiX, load + failure testing, release notes, ONNX artifacts, final docs, and load test evidence).  
- [x] PowerShell installer/updater helper for manual Windows deployments (`tools/install-service.ps1`).

## MCP Command Implementation Checklist
1. Keep DTOs/validators synced with generated JSON Schema and describe payload.
2. Route `/mcp/{project}/...` commands through `MemoryRouter`, reusing permission+tier logic.
3. Add Streamable transport tests (include canonical/permanent coverage).
4. Revise docs/usage recipes whenever tools change.
5. Track CRC responsibilities in `docs/spec.md` when services evolve.
