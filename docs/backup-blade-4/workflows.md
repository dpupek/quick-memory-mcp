## Workflows

### Configure backup settings
1) Admin opens Settings → Maintenance → Backup blade.
2) Blade fetches current `global.backup` options and last probe/health messages.
3) Admin edits target path (local/UNC) and cron schedules (differential, full) with retention sliders.
4) Admin clicks "Test write"; UI calls probe endpoint; result shown inline.
5) Admin saves; backend persists config and reloads options; success toast + next-run preview updates.

### Run manual backup
1) Admin opens blade and chooses endpoint + mode (differential/full).
2) Clicks "Run now"; request enqueues backup.
3) Activity feed shows queued → success/failure; errors surface message and log link.

### Monitor backup health
1) Blade displays health status chip and issues from `/health` (e.g., unwritable target, last failed attempt per endpoint).
2) Activity feed shows latest attempts; a failed latest attempt marks the endpoint degraded until the next success.
