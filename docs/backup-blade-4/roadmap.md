## Roadmap / tasks

- [ ] API: extend admin config endpoint to read/write `global.backup` (targetPath, crons, retention); add write-probe endpoint.
- [ ] Activity/Audit: add `BackupActivityStore` (bounded log, e.g., last 200) that also writes audit-style entries: timestamp, endpoint, mode, status, message, duration, initiatedBy, instanceId; surface via admin API.
- [ ] UI: build Backup blade (target input + probe, cron editors, retention sliders, next-run preview, activity table with audit entries, manual run buttons).
- [ ] Health: derive backup health per endpoint from latest attempt (success clears failure); probe/unwritable target remains separate issue; surface in `/health`.
- [ ] Permissions: enforce Admin tier for blade + endpoints; show read-only state for lower tiers.
- [ ] Telemetry/tests: unit/e2e tests for probe, save, manual run flows; verify health integration and activity feed.
