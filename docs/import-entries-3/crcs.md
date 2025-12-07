# CRCs – Bulk Entries Import

## ImportEntries MCP Tool / Admin Endpoint

- **Responsibilities**
  - Accept endpoint, mode, dryRun, and content.
  - Parse JSONL/JSON into `MemoryEntry` instances.
  - Validate entries and compute per-entry outcomes.
  - Apply changes to the target store when not in dry-run mode.
  - Return a summary of results and errors.
- **Collaborators**
  - `MemoryRouter` / `MemoryStore` for store resolution and writes.
  - `MemoryEntryValidator` for normalization and schema checks.
  - `JsonlRepository` for `replace` mode (atomic file swaps).

## MemoryStore / JsonlRepository

- **Responsibilities**
  - Persist normalized entries.
  - Support atomic replacement of `entries.jsonl` when requested.
- **Collaborators**
  - Import tool for bulk writes.

