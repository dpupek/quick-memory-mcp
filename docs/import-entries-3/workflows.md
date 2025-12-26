# Workflows – Bulk Entries Import

## Workflow 1 – Dry-Run Import into a Project

1. Admin selects a target project (endpoint) in the Admin Web UI or CLI.
2. Admin prepares a JSONL or JSON file containing entries.
3. Admin calls the import feature with:
   - `endpoint = "<project-key>"`,
   - `mode = "upsert"`,
   - `dryRun = true`,
   - `content` = file contents.
4. Server:
   - Parses entries.
   - Validates each entry (project/id, schema).
   - Computes what would happen under `upsert`:
     - how many entries would be updated/inserted,
     - how many would be skipped or rejected.
5. Admin reviews the summary and decides whether to proceed.

## Workflow 2 – Apply Import (Upsert)

1. After a successful dry run, admin calls the import feature again with
   `dryRun = false` and the same content.
2. Server:
   - Re-validates entries.
   - For each valid entry:
     - Updates existing ids or inserts them (upsert).
3. Server returns a summary (imported, updated, skipped) and logs a
   structured audit message.

## Workflow 3 – Append-Only Import

1. Admin wants to import entries from another project/server but avoid
   overwriting any existing entries.
2. Admin uses `mode = "append"`.
3. Server:
   - Treats entries whose ids already exist as skipped.
   - Inserts only new ids.
4. Summary reports how many entries were skipped to avoid duplicates.

## Workflow 4 – Replace Import (Destructive)

1. Admin needs to replace a project’s entries with a known-good set
   (e.g., from a backup or another environment).
2. Admin uses `mode = "replace"` and must pass additional confirmation
   (e.g., a flag or explicit acknowledgement).
3. Server:
   - Validates all incoming entries.
   - Writes a new JSONL file to disk (atomic swap) containing only the
     imported entries.
4. Summary confirms the replacement; logs record the operation.
