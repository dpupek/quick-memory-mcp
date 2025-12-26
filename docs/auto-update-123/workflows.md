# Epic 123 – Auto-Update Workflows

## Personas

- **Admin** – Responsible for operating the Quick Memory service on a Windows host. Comfortable with basic git and PowerShell but prefers a simple, guided UI for routine updates.
- **Agent user** – Works inside Codex/ChatGPT and relies on Quick Memory; cares that updates do not disrupt data or config.

## Workflow 1 – Check for Updates

1. Admin opens the Admin Web UI and navigates to the **Config (TOML)** or **Health** tab.
2. Admin clicks **Check for updates**.
3. Server:
   - Reads the configured `updateBranch` (e.g., `release-1.2.x`) from config or a small manifest file.
   - Runs `git fetch origin` in the repo root.
   - Compares the local HEAD of `updateBranch` to `origin/updateBranch`.
4. Admin Web UI displays:
   - Current branch and commit (short SHA).
   - Latest remote commit for the branch.
   - A boolean flag: `updateAvailable = true/false`.

## Workflow 2 – Apply Update from the Admin Web UI

1. Admin sees `updateAvailable = true` and clicks **Apply update**.
2. Admin Web UI shows a confirmation dialog:
   - Explains that:
     - Config (`QuickMemoryServer.toml`) and data (`MemoryStores`) will not be touched.
     - The service will briefly restart.
3. On confirmation, the Admin Web UI issues `POST /admin/update/apply` with:
   - `mode = "apply"`.
   - Optional comment (for audit trail).
4. Server enqueues an update job and immediately returns `202 Accepted` with a job identifier.
5. Background update job executes:
   1. `git fetch origin && git checkout <updateBranch> && git pull origin <updateBranch>`.
   2. `dotnet build QuickMemoryServer.sln -c Release` (+ optional `dotnet test`).
   3. If build/test fails → record failure, log details, mark job as failed; **do not** run installer.
   4. If validation succeeds:
      - Run `tools/install-service.ps1` with flags that:
        - Stop the service.
        - Copy new binaries into the install location.
        - Preserve `QuickMemoryServer.toml`.
        - Never touch `MemoryStores`.
      - Start the service again.
6. Admin Web UI polls `GET /admin/update/status` to show:
   - `status`: pending / succeeded / failed.
   - `branch`, `fromCommit`, `toCommit`.
   - Last error message (if any).

## Workflow 3 – Validation-Only Check (Dry Run)

1. Admin selects **Validate update** (or a “dry run” option).
2. Admin Web UI POSTs `mode = "validate"` to `/admin/update/apply`.
3. Server runs steps:
   - `git fetch/pull`, `dotnet build`, optional `dotnet test`.
   - **Does not** invoke the installer or restart the service.
4. Admin Web UI shows the result:
   - “Update validated successfully; safe to apply.”
   - Or: “Validation failed: <build/test error>”.
