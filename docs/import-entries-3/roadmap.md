# Epic 3 – Bulk Entries Import Roadmap

## Phase 0 – Design

- [ ] Decide on the exact import contract:
  - [ ] MCP tool name and parameters (e.g., `importEntries`).
  - [ ] Admin HTTP endpoint shape (if any).
  - [ ] Accepted formats (JSONL vs JSON array) and autodetection rules.

## Phase 1 – Implementation

- [ ] Implement import parsing & validation:
  - [ ] Parse content into `MemoryEntry` objects.
  - [ ] Use `TryPrepareEntry` and `MemoryEntryValidator.Normalize` for
        each entry.
  - [ ] Collect per-entry successes and errors.
- [ ] Implement modes:
  - [ ] `upsert` – update or insert by id.
  - [ ] `append` – insert only new ids, skip existing.
  - [ ] `replace` – atomically swap `entries.jsonl` with the new set
        (Admin-only, guarded).
- [ ] Implement `dryRun` flow that skips disk writes but returns full
      summary.

## Phase 2 – Admin UI / CLI Integration

- [ ] Add a minimal admin SPI/CLI surface:
  - [ ] `POST /admin/import/{endpoint}` HTTP route (Admin-only), or
  - [ ] Documented MCP tool usage for CLI-based imports.
- [ ] Optionally add a simple SPA panel:
  - [ ] Text area/file upload for content.
  - [ ] Mode + dryRun controls.
  - [ ] Result summary display.

## Phase 3 – Docs & Hardening

- [ ] Update `docs/spec.md` and `docs/agent-usage.md` / admin docs to
      describe the import feature.
- [ ] Add tests for:
  - [ ] Project mismatch handling.
  - [ ] Invalid schema rejection.
  - [ ] `upsert` vs `append` behavior.
  - [ ] `replace` failure when any entry is invalid.

