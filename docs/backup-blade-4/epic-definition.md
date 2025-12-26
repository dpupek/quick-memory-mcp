## Epic: Backup Management Blade (Case #4)

**Big idea**: Provide an admin-only Admin Web UI blade to configure backups (target path, schedules, retention) and monitor recent backup activity, reducing failed backups due to misconfiguration and making recovery audits visible.

**Success criteria**
- Admins can set and save backup target path (including UNC) and cron schedules via UI, with validation and write-probe feedback.
- Backup activity feed shows at least the last 50 events with status, endpoint, mode, and message.
- Manual “Run backup now” per endpoint works and surfaces result.
- Health status integrates: unwritable paths or failed runs appear in UI and `/health` remains consistent.

**Out of scope**
- Restores/rollback UI.
- Multi-tenant per-user override of schedules.
