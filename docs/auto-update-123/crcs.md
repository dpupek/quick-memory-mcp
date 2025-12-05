# Epic 123 – Auto-Update CRCs

## UpdateService

- **Responsibilities**
  - Orchestrate “check for update” and “apply update” operations.
  - Enqueue and track update jobs.
  - Ensure only one update job runs at a time.
- **Collaborators**
  - `AdminConfigService` (to read global/update settings if stored in config).
  - `ILogger<UpdateService>` for diagnostics.
  - OS shell (PowerShell / git / dotnet CLI).
  - `BackupService` (optional future fetch to take a snapshot before upgrade).

## UpdateJob

- **Responsibilities**
  - Encapsulate a single update operation (branch, from/to commit, mode).
  - Run git fetch/pull, build/test, installer script, and service restart steps.
  - Record status, timings, and outcome.
- **Collaborators**
  - `UpdateService`.
  - `ILogger`.

## Installer Script (`tools/install-service.ps1`)

- **Responsibilities**
  - Publish and install updated binaries into the service directory.
  - Stop the Windows service, copy binaries, start the service.
  - Preserve `QuickMemoryServer.toml` and data files.
- **Collaborators**
  - Windows Service Control Manager.
  - File system.

## Admin Update Endpoints

- **Responsibilities**
  - Expose HTTP endpoints:
    - `POST /admin/update/check`
    - `POST /admin/update/apply`
    - `GET /admin/update/status`
  - Enforce Admin-tier authorization.
  - Translate HTTP payloads to `UpdateService` calls.
- **Collaborators**
  - `ApiKeyAuthorizer`.
  - `UpdateService`.
  - `ILogger`.

## SPA Update Panel

- **Responsibilities**
  - Provide UI controls:
    - Check for updates.
    - Apply update (with confirmation).
    - Show last update result.
  - Call the admin update endpoints and render responses.
- **Collaborators**
  - Admin update endpoints (`/admin/update/*`).
  - Existing Health/Config blades (for discovery and placement).

