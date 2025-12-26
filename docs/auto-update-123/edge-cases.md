# Epic 123 – Auto-Update Edge Cases & Risks

## Git / Source Issues

- **Dirty working tree**: Local repo has uncommitted changes (e.g., hotfixes) that block `git pull`.
  - Behavior: Update job fails with a clear error (“working tree not clean; please commit or stash changes”).
  - Mitigation: Do not auto-stage/commit. Require manual cleanup by the operator.

- **Branch mismatch**: Configured `updateBranch` does not exist on the remote.
  - Behavior: `git fetch` succeeds but `git checkout` or `git pull` fails.
  - Mitigation: Surface a clear error and recommend updating `updateBranch` in config.

- **Network failure**: Remote unreachable or authentication failure to git.
  - Behavior: Update job fails; no binaries changed.
  - Mitigation: Log, surface message in the Admin Web UI, allow retry later.

## Build/Test Failures

- **Build fails**:
  - Behavior: Installer is never invoked. Existing binaries remain intact.
  - Mitigation: Capture build stderr/stdout into logs and expose a summarized error in update status.

- **Tests fail**:
  - Behavior: Same as build failure; abort before touching installed binaries.
  - Mitigation: Optional; can be configurable (strict mode vs build-only mode).

## Installer / Service Issues

- **Installer script throws**:
  - Behavior: Service may already be stopped; copy may be partial if the script fails mid-way.
  - Mitigation:
    - Prefer copying to a temp location first, then into the install directory.
    - Ensure script stops service before copy and restarts it at the end.
    - Treat any error as “update failed”; record messages.

- **Service fails to restart**:
  - Behavior: Installer completes copy but the service does not start.
  - Mitigation:
    - Mark update job as failed with a very clear message.
    - Suggest manual restart and health check instructions.

## Config & Data Safety

- **Config overwrite**:
  - Must never happen via auto-update.
  - Installer must:
    - Only create `QuickMemoryServer.toml` if missing, or
    - Require an explicit flag (not used by auto-update) to overwrite.

- **Data overwrite**:
  - Auto-update must never delete or overwrite content in `MemoryStores`.
  - All update operations must target binaries/docs/wwwroot/tools only.

## Re-Entrancy / Concurrency

- **Multiple update requests**:
  - Behavior: If an update job is already running, subsequent `apply` requests should be rejected or queued with a “job already in progress” message.

## Observability Gaps

- **Silent failures**:
  - Risk: Build or install failure not surfaced clearly leads to confusion (“update did nothing”).
  - Mitigation:
    - Always log errors with a specific event ID / prefix (`qms_update_*`).
    - Ensure the Admin Web UI shows last job result (success/failure + brief reason).
