# Stakeholder Summary – Bulk Entries Import

## Admins / Operators

- Gain a safe, repeatable way to:
  - Seed new projects from existing data.
  - Copy entries between environments.
  - Restore from offline backups or external tools.
- Can use dry-run mode to validate imports before committing changes.

## Developers / Teams

- Can bootstrap projects with known-good knowledge without manually
  re-entering entries.
- Can script migrations (e.g., from other systems) into Quick Memory
  using a documented format.

## AI Agents

- May be used to **prepare** import payloads (e.g., transforming
  external JSON into `MemoryEntry` JSONL), but the final import remains
  an admin-controlled operation.

