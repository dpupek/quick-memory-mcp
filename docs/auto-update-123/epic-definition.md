# Epic 123 – One-Click Auto-Update from the Admin Web UI

## Big Idea

Allow an admin to trigger a **non-destructive binary update** of Quick Memory Server directly from the embedded Admin Web UI, without touching configuration or data files. The server should:

- Pull the latest code for a configured **release branch** (e.g., `release-1.2.x`).
- Build and validate the new version.
- If validation succeeds, overwrite the installed binaries and restart the service.
- Never overwrite `QuickMemoryServer.toml` or any `MemoryStores` data.

## Success Criteria

- Admins can:
  - See the currently installed version and release branch.
  - Check whether an update is available for the configured release branch.
  - Apply an update from the Admin Web UI, with clear confirmation that config/data are safe and that the service will briefly restart.
- The update pipeline:
  - Builds/tests the solution before copying any binaries into the install directory.
  - Uses the existing `install-service.ps1` script so all copy/restart behavior stays centralized.
  - Blocks updates if the build/test step fails, leaving the current binaries untouched.
- Config and data protections:
  - `QuickMemoryServer.toml` is never overwritten by the auto-update path.
  - `MemoryStores/*` is never deleted or overwritten.
- Observability:
  - Update attempts (branch, commit, result) are logged in the main log and visible in the Admin Web UI.
  - Failures surface human-readable error messages (e.g., git failure, build failure, test failure, install failure).

## Non-Goals

- Multi-version side-by-side deployments or automatic rollback logic (can be a later epic).
- Arbitrary branch selection from the Admin Web UI (initially a single configured release branch).
- Auto-merging or resolving git conflicts on the server.
