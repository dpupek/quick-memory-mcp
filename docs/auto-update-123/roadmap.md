# Epic 123 – Auto-Update Roadmap

## Phase 0 – Design & Plumbing

- [ ] Decide where to store `updateBranch` and current commit (config vs. manifest file).
- [ ] Document the expected git remote and branch naming (`release-x.y.z`).
- [ ] Validate that `install-service.ps1` already preserves `QuickMemoryServer.toml` and `MemoryStores`.

## Phase 1 – Backend Update Service

- [ ] Implement `UpdateService` with:
  - [ ] A queue for update jobs (modes: `check`, `validate`, `apply`).
  - [ ] Internal job execution logic (git fetch/pull, build/test, installer invocation).
  - [ ] Status tracking (last job result, timestamps, branch/commit details).
- [ ] Add structured logging for each step (git, build, test, install, restart).

## Phase 2 – Admin Endpoints

- [ ] Add `POST /admin/update/check` (Admin-only).
- [ ] Add `POST /admin/update/apply` with `mode` field (`validate` vs `apply`).
- [ ] Add `GET /admin/update/status` that returns the last job result.
- [ ] Ensure all three endpoints reuse existing Admin authorization logic.

## Phase 3 – Admin Web UI Integration

- [ ] Add an Update section to either **Config (TOML)** or **Health** tab:
  - [ ] Display current branch/commit and configured `updateBranch`.
  - [ ] “Check for updates” button wired to `/admin/update/check`.
  - [ ] “Apply update” button wired to `/admin/update/apply` with confirmation.
  - [ ] Status display bound to `/admin/update/status`.
- [ ] Add warning/confirmation copy emphasizing:
  - [ ] Config and data are not touched.
  - [ ] Service will briefly restart.

## Phase 4 – Hardening & Edge Cases

- [ ] Handle dirty working tree errors gracefully (require manual cleanup).
- [ ] Block concurrent updates (return “job already in progress”).
- [ ] Improve error messages for git/network failures.
- [ ] Add optional pre-update backup hook (e.g., call `BackupService` for each endpoint).

## Phase 5 – Documentation & Help

- [ ] Update `docs/admin-ui-help.md` with a short “Auto-update” section explaining the UI.
- [ ] Update `docs/spec.md` in the “Admin Console & Configuration APIs” section to describe the update endpoints and constraints (config/data safety).
- [ ] Optionally add a short blurb in `docs/end-user-help.md` or `resource://quick-memory/help` that explains update behavior for operators.
