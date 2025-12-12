## Edge cases / risks
- Target path not writable (ACL/UNC offline): probe fails; health shows degraded; manual runs must fail fast with clear message.
- Invalid cron strings: prevent save; show validation error; keep last good config.
- Multi-instance deployment: config change applies per instance; activity feed should indicate instance if available.
- Long-running backups: show in-progress; avoid concurrent full/differential on same endpoint; ensure activity captures duration even on timeout.
- No endpoints configured: blade shows warning and disables manual runs.
- Clock skew: cron previews could be off; rely on server-evaluated "next run".
