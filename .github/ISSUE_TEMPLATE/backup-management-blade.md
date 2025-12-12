---
name: "Feature: Backup Management Blade"
about: Admin UI to configure backup target/schedules and view backup activity
labels: ["feature", "ui", "backup"]
---

## Summary
Implement an admin-only Backup Management blade in the SPA to configure backup target path, schedules/retention, and to view recent backup activity with health signals.

## Background / Motivation
- Admins need to point backups at network paths/alternate volumes and confirm writability.
- Scheduled backups fail silently today; no UI to see last success/failure.
- Health should reflect the latest backup attempt per endpoint; probes should surface unwritable targets early.

## Requirements
- Read/write `global.backup` (targetPath, differentialCron, fullCron, retentionDays, fullRetentionDays).
- Probe endpoint to test writability of the target path (local or UNC); record health issue on failure, clear on success.
- Manual “Run backup now” (differential/full) per endpoint; enqueue via existing BackupService.
- Activity/audit feed (latest 50+) showing timestamp, endpoint, mode, status, message, duration, initiatedBy, instanceId.
- Health: endpoint is healthy if the latest backup attempt succeeded; failed latest attempt shows degraded state. Unwritable target remains a separate health issue.
- Admin-only access; read-only message for lower tiers.

## Acceptance Criteria
- Settings save validates cron/retention and persists targetPath; invalid inputs return clear errors.
- Probe failure shows inline error and logs audit + health issue; success clears target-path issue.
- Activity feed updates when manual or scheduled backups run; capped list returns newest-first.
- `/health` and the blade show consistent degraded state after a failed backup until a success occurs.
- Manual run returns queued response and appears in feed with outcome.

## Testing
- Unit: probe success/failure health behavior; latest-attempt health logic; cron validation.
- Integration: settings GET/POST round-trip; activity feed ordering; manual run enqueues; health reflects failures.

## Notes
- Activity storage can be in-memory ring buffer + optional audit sink; persistence beyond memory is a stretch goal.
- Do not change MCP tools for this; HTTP admin endpoints only.
