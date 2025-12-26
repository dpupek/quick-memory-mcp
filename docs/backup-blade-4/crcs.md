## CRCs (initial)

- `BackupService` (existing): runs scheduled/manual backups; emits health issues on failure/unwritable target; exposes activity metrics.
- `HealthReporter` (existing): stores health issues; exposes in `/health` and `health` tool.
- `AdminConfigController` (new/extended): read/write `global.backup` settings; probe target path.
- `BackupActivityStore` (new): append-only audit-style log (bounded, persists if audit sink present) with endpoint, mode, status, message, duration, initiatedBy, timestamp, instanceId.
- Admin Web UI Backup Blade: surfaces config, probes, activity feed, manual run actions; calls AdminConfig + Backup endpoints.
