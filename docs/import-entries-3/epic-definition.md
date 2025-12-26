# Epic 3 – Bulk Entries Import (JSONL / JSON)

## Big Idea

Provide a safe, admin-only bulk import mechanism for entries so that
teams can:

- Seed new projects with existing knowledge.
- Migrate entries between servers or projects.
- Restore data from external tools or backups.

The import path should:

- Reuse the existing `entries.jsonl` format.
- Validate each entry using the same rules as normal MCP tools.
- Support dry runs (validation only) and multiple import modes.

## Success Criteria

- A new Admin-only import capability exists that:
  - Accepts an `endpoint` (project key), a `mode` (`upsert` / `append`
    / `replace`), and `dryRun` flag.
  - Accepts either JSONL (one `MemoryEntry` per line) or a JSON array
    of entries.
  - Validates every entry via `TryPrepareEntry` and
    `MemoryEntryValidator.Normalize`.
- Behavior:
  - `upsert` – updates existing ids or inserts new ones.
  - `append` – inserts new ids only; skips entries whose ids already
    exist.
  - `replace` – replaces the project’s `entries.jsonl` with the imported
    set (admin-only, clearly documented as destructive).
- Dry-run mode:
  - Parses and validates entries without writing to disk.
  - Returns a summary of accepted, skipped, and invalid entries plus
    error details per line.
- Admin Web UI / CLI:
  - The admin UI or CLI can trigger imports and see dry-run results.

## Non-Goals

- Generic CSV or arbitrary schema imports; this epic focuses on JSONL /
  JSON that matches `MemoryEntry`.
- Per-entry interactive resolution (e.g., conflict wizards); imports are
  batch operations with clear, summarized results.
- Automated, implicit migrations at startup; imports are explicit,
  operator-driven actions.
