# Edge Cases – Bulk Entries Import

- **Unknown endpoint**
  - Import attempted for an endpoint that does not exist.
  - Behavior: return an error similar to other tools
    (`Endpoint '<key>' is not available.`).

- **Project mismatch**
  - Entries contain `project` values that differ from the target
    endpoint.
  - Behavior: `TryPrepareEntry` rejects them with a
    `project-mismatch` error; they appear in the error summary.

- **Invalid schema**
  - Entries fail `MemoryEntryValidator.Normalize` (invalid
    `curationTier`, wrong embedding length, etc.).
  - Behavior: entry is rejected with a clear error message; other
    entries still processed.

- **Mixed JSONL / JSON array**
  - Content is not clearly JSONL or a single array.
  - Behavior: return a top-level parse error and treat nothing as
    imported; recommend correcting the file format.

- **Replace mode and partial failure**
  - Some entries are invalid in `replace` mode.
  - Behavior: refuse to write anything; report errors and require a
    clean, fully-valid set before performing a replacement.

- **Concurrency**
  - Import runs while other operations update the same endpoint.
  - Behavior: rely on existing `MemoryStore` locking; imports use the
    same write path (`UpsertAsync` / `DeleteAsync`) and thus serialize
    with other writers.

