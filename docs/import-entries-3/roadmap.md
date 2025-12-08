# Epic 3 – Bulk Entries Import Roadmap

## Phase 0 – Design

- [x] Decide on the exact import contract:
  - [x] MCP tool name and parameters (defer; start with HTTP admin route).
  - [x] Admin HTTP endpoint shape (`POST /admin/import/{endpoint}`).
  - [x] Accepted formats (JSONL vs JSON array) and autodetection rules.

## Phase 1 – Implementation

- [x] Implement import parsing & validation:
  - [x] Parse content into `MemoryEntry` objects.
  - [x] Use `TryPrepareEntry` and `MemoryEntryValidator.Normalize` for
        each entry.
  - [x] Collect per-entry successes and errors.
- [x] Implement modes:
  - [x] `upsert` – update or insert by id.
  - [x] `append` – insert only new ids, skip existing.
  - [ ] `replace` – atomically swap `entries.jsonl` with the new set
        (Admin-only, guarded).
- [x] Implement `dryRun` flow that skips disk writes but returns full
      summary.

## Phase 2 – Admin UI / CLI Integration

- [x] Add a minimal admin SPI/CLI surface:
  - [x] `POST /admin/import/{endpoint}` HTTP route (Admin-only), or
  - [ ] Documented MCP tool usage for CLI-based imports.
- [x] Optionally add a simple SPA panel:
  - [x] Text area/editor for content (Monaco-based).
  - [x] Mode + dryRun controls.
  - [x] Result summary display.

## Phase 3 – Docs & Hardening

- [ ] Update `docs/spec.md` and `docs/agent-usage.md` / admin docs to
      describe the import feature.
- [ ] Add tests for:
  - [ ] Project mismatch handling.
  - [ ] Invalid schema rejection.
  - [ ] `upsert` vs `append` behavior.
  - [ ] `replace` failure when any entry is invalid.
