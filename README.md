# Quick Memory Server

<p align="left">
  <img src="assets/robot_logo_512.png" alt="Quick Memory Server logo" width="128" />
</p>

Quick Memory Server is a lightweight, self-hosted memory service for MCP tools and agents. Use it to keep durable, searchable context across sessions and handoffs: decisions, procedures, investigations, and operational runbooks that shouldn’t live only in chat scrollback.

This repository contains a Windows-service-hosted ASP.NET Core worker that exposes:
- An embedded Admin Web UI for projects/users/entries/config/health.
- Streamable HTTP MCP endpoints for agent/tool integrations.
- Per-project memory stores (plus optional shared memory) with hybrid search and graph relations.

## Why you’d use this
- **Agents forget; teams churn.** Persist “what we learned” so the next session/agent doesn’t redo investigations.
- **Keep ops knowledge close to the system.** Health, logs, metrics, and backups live alongside the memory store.
- **Project isolation.** Separate memory stores by project/branch while still allowing shared, cross-project reference entries.

## Docs (start here)
- [`docs/spec.md`](docs/spec.md) – architecture and command surface
- [`docs/plan.md`](docs/plan.md) – implementation plan / phases / epics
- [`docs/end-user-help.md`](docs/end-user-help.md) – end-user guide + Codex setup
- [`docs/agent-usage.md`](docs/agent-usage.md) – MCP-first recipes and payload shapes
- [`docs/codex-workspace-guide.md`](docs/codex-workspace-guide.md) – Codex configuration patterns (`mcp-proxy` / `mcp-remote`)
- [`docs/admin-ui-help.md`](docs/admin-ui-help.md) – Admin Web UI walkthrough (blades, where code lives)

## Getting Started

### Prereqs
- Install the **.NET 9 SDK preview (or newer)**.

### Run locally (dev)
1. Copy `QuickMemoryServer.sample.toml` to `QuickMemoryServer.toml` and adjust endpoints/users for your environment.
2. Build from the repository root:
   ```bash
   dotnet build QuickMemoryServer.sln
   ```
   WSL note: if you’re working under `/mnt/*`, see [`AGENTS.md`](AGENTS.md) for the recommended Windows `dotnet.exe` + Git setup to avoid slow I/O.
3. Optional: run tests:
   ```bash
   dotnet test
   ```
4. Run the worker as a console app:
   ```bash
   dotnet run --project src/QuickMemoryServer.Worker
   ```
5. Confirm it’s up:
   - `GET /health` for server status and store inventory
   - `GET /metrics` for Prometheus metrics
   - `GET /docs/schema` for the runtime JSON Schema (cached with ETag)
6. Open the Admin Web UI at `/` to:
   - Create/edit projects (endpoints)
   - Create/edit users + project permissions (API keys)
   - Browse/search/edit memory entries
   - View Health & Logs and download log bundles

### Connect an agent (Codex)
The canonical, copy/paste-ready Codex setup lives in:
- [`docs/end-user-help.md`](docs/end-user-help.md) (quick start)
- [`docs/codex-workspace-guide.md`](docs/codex-workspace-guide.md) (multiple patterns, env-var options)

At a minimum, you’ll need:
- MCP base URL: `http://localhost:5080/mcp`
- Auth header: `X-Api-Key: <your key>` (create keys via the Admin Web UI)

## Windows installer/updater

For Windows deployments, use the PowerShell helper [`tools/install-service.ps1`](tools/install-service.ps1) (run in an elevated prompt):

```powershell
powershell -ExecutionPolicy Bypass -File tools/install-service.ps1
```

- Publishes the worker + MemoryCtl, rewrites `QuickMemoryServer.toml`, copies payloads to `C:\Program Files\q-memory-mcp` by default, and (re)creates the `QuickMemoryServer` Windows service (display name: Quick Memory MCP).
- Prompts for machine/local install, install/data dirs, port, service account, and API keys; you can pre-supply args (`-InstallDirectory`, `-DataDirectory`, `-Port`, `-ServiceAccount`, `-SkipStart`, `-SkipFirewall`, `-NoRollback`).
- If `QuickMemoryServer.toml` already exists, it asks before overwriting; data files are never overwritten.
- Stops the service before copy to avoid file locks; restarts it unless `-SkipStart` is used. Copies `docs/` so admin/agent help render.

The service is pre-configured to run as a Windows service (`AddWindowsService`) when published and installed with WiX (see [`docs/plan.md`](docs/plan.md)).

## Key URLs
- `/` – Admin Web UI
- `/health` – JSON health report
- `/metrics` – Prometheus metrics
- `/mcp` – MCP base route (streamable HTTP)
- `/docs/schema` – runtime JSON Schema (cached + ETag)
- `/admin/help/end-user` – end-user help
- `/admin/help/agent` – agent help (field guide + recipes)
- `/admin/help/codex-workspace` – Codex workspace guide

## Repository Layout

- `docs/` – Architecture spec and implementation plan.
- `src/QuickMemoryServer.Worker/` – Worker service project.
- `layout.json` – Installer layout manifest used by the WiX packaging process.
- `QuickMemoryServer.sample.toml` – Sample configuration file.
- `AGENTS.md` – Contributor guide for day-to-day development details.
- `tools/MemoryCtl` – Lightweight CLI for invoking MCP maintenance commands (e.g., backups).
- `src/QuickMemoryServer.Worker/logs/` – Rolling structured logs (created at runtime, dev-only).

## Next Steps

See [`docs/plan.md`](docs/plan.md) and GitHub Issues for the active roadmap. Epics for this repo are tracked as GitHub issues; use the GitHub CLI (`gh issue view`, `gh issue edit`) during development to keep scope and acceptance criteria up to date.
