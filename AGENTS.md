# Repository Guidelines

## Project Structure & Module Organization
- The worker service lives in `src/QuickMemoryServer.Worker/`; match new code to existing folders (`Configuration/`, `Memory/`, `Models/`, `Services/`).
- `docs/` tracks the spec, the evolving `plan.md`, usage recipes, and schema/agent guides; update them before touching runtime behavior.
- Runtime aids and tooling are under `tools/` (backup CLI, watchers) and `load-tests/` (k6 scripts); platform-specific installer layout lives in `layout.json`.

## Build, Test, and Development Commands
- Always run `"/mnt/c/Program Files/Git/bin/git.exe" status` to confirm staging, and `"$NEXPORT_WINDOTNETdotnet.exe" test` (or Roslyn `TestSolution`) after code changes to catch validation/build regressions early.
- Set `NEXPORT_WINDOTNET` to `"/mnt/c/Program Files/dotnet/"` once per shell so scripts use `"$NEXPORT_WINDOTNETdotnet.exe" build ...`, `test`, and `run` (same binary for CLI, restore, publish).
- For git operations under WSL, prefer `"/mnt/c/Program Files/Git/bin/git.exe" status` (note timeline slowdowns on native Linux Git).
- `"$NEXPORT_WINDOTNETdotnet.exe" build QuickMemoryServer.sln` compiles everything with the required .NET 9 preview.
- `"$NEXPORT_WINDOTNETdotnet.exe" test` runs all xUnit suites; `run --project src/QuickMemoryServer.Worker` launches the worker for manual validation (watch logs for `Listening on http://localhost:5080`).
- Windows installs/updates: use `tools/install-service.ps1` (run elevated). It publishes the worker + MemoryCtl, copies to `C:\Program Files\q-memory-mcp` by default, prompts for install/data dirs, port, service account, and API keys, stops the service before copy to avoid file locks, and restarts unless `-SkipStart` is set. `QuickMemoryServer.toml` is only overwritten if confirmed; data files are never overwritten. Flags: `-SkipFirewall`, `-ValidateOnly`, `-NoRollback`, `-Uninstall`.

## Coding Style & Naming Conventions
- C# 12, file-scoped namespaces, nullable enabled, `var` for obvious declarations, PascalCase for types/methods, camelCase for locals/parameters; `*Service` suffix on services.
- Keep new files ASCII; mention new localizable strings when adding user-facing copy.
- Prefer DI-based services over static singletons; option/config classes live under `Configuration/`.

## Testing Guidelines
- Tests use xUnit + FluentAssertions; keep naming `{MethodUnderTest}_{Scenario}_{ExpectedOutcome}`.
- Add coverage for persistence, search, MCP mappings, and file watchers; include canonical/permanent edge cases.
- Run `"$NEXPORT_WINDOTNETdotnet.exe" test` before pairing changes; document skipped tests with TODO comments referencing follow-up cases.

## Commit & Pull Request Guidelines
- Use conventional commits that reference the relevant GitHub issue (e.g., `feat(memory): add streamable mcp transport (closes #1)`).
- PRs must describe what changed, reference spec sections (`docs/spec.md`), include tests run, link the relevant `docs/plan.md` iteration, and attach installer/log artifacts when applicable.
- Always ask for an issue number before committing; do **not** auto-commit.

## Documentation & Planning Discipline
- Update `docs/plan.md` with checkboxes/phases for every new epic; call out completed phases (0–7) and the next focus (streamable MCP implementation + schema). Link decisions/questions directly in the plan file.
- Keep `docs/spec.md` synchronized with runtime changes (memory kinds, curation tiers, embedded schema URLs) and document observations/recipes in `docs/agent-usage.md`.
- Regenerate references to `agent-usage` when introducing new MCP tools or recipes (search, project checks, entry change detection).

## MCP & Observability Conventions
- Prefer the Model Context Protocol C# SDK (`ModelContextProtocol` NuGet) for the Streamable HTTP transport; expose `MapMcp` endpoints that reuse existing services and describe tooling via `describe`/`getUsageDoc`.
- Monitor MCP ops via Serilog/EventCounters (`qms_*` metrics) and expose `/health` plus Prometheus instrumentation.
- Use the Roslyn MCP help resource (`resource://roslyn/help`) and the `roslyn_code_navigator` tools (`SearchSymbols`, `FindReferences`, etc.) for complex lookups.
- Document new MCP commands, config samples (TOML + `.codex/config.toml`), and Tier matrices before shipping.
- When using `mcp-remote` as a proxy to Quick Memory MCP, a workable config is:
  ```toml
  [mcp_servers.quick-memory]
  command = "npx"
  args = ["mcp-remote@latest","http://localhost:5080/mcp","--header","X-Api-Key:$AUTH_TOKEN","--allow-http","--debug"]
  env = { AUTH_TOKEN = "/K/XodEPueCMorpZV8qKP47svleB0FQ9jmMVtIXO+Lw=" }
  ```
  Notes: `mcp-remote` caches auth under `~/.mcp-auth`; if keys change, delete that folder. Prefer `X-Api-Key` header for Quick Memory; bearer also works. Ensure `global.httpUrl` binds to `0.0.0.0:5080` and Windows firewall allows 5080 so WSL → Windows works.

## Quick Memory Usage (MCP)

- **Default project:** Use `qm-proj` unless the user says otherwise. Call `listProjects` if unsure.
- **Project notes update:** Recent GH issue created for the Backup Management Blade is logged in `qm-project-notes` (entry id `qm-project-notes:a1e2fb9448a14c8ba568e27baec6d2e2`); include lessons about gh labels when creating issues.
- **Admin UI is Razor-backed:** The Admin Web UI is served from `Views/Admin/Index.cshtml` (not just `/wwwroot/index.html`). Blades can be lazy-loaded from fragments (Backup uses `wwwroot/fragments/backup.html`). Update cshtml/nav + fragment + `wwwroot/js/app.js` when adding blades.
- **First minute:** Run `health`, then `listProjects`, then `listRecentEntries { endpoint: "qm-proj", maxResults: 20 }` to catch up. Surface any gaps to fill.
- **Cold start recipe (2025-12-09):** If `coldStart` fails on `qm-proj`, run `health` and `listProjects`, then call `coldStart` with `endpoint="qm-project-notes"` and `epicSlug="qm-proj"` (this returned the canonical cold-start entries: WSL/Windows .NET guidance, epic shaping, AAAA pattern, follow code smells).
- **Recording lessons:** Use `upsertEntry` with `project = "qm-proj"`, leave `id` empty to auto-generate, pick a clear `kind` (`note`/`procedure`/`decision`), add 3–6 tags, and set `curationTier` (`provisional`→`curated`→`canonical`) as appropriate. Avoid `isPermanent=true` unless it is a long-lived rule; Admin is required to delete permanents.
- **Prompts:** Prefer the curated prompts in `prompts-repository` via `prompts/list` + `prompts/get` (e.g., `onboarding:first-time`, `cold-start:project`). They already include argument metadata and categories.
- **Graph & search:** Use `searchEntries { endpoint: "qm-proj", text, includeShared }` for retrieval and `relatedEntries { endpoint: "qm-proj", id, maxHops }` for curated links. Shared inclusion follows the project’s defaults; override per call when needed.
- **Backups & audits:** `requestBackup` is Admin-only; permission edits are logged to the audit trail. When rotating keys, update Codex config and restart the client to pick up new headers.
- **Epics tracking:** Epics for this project are tracked against GitHub issues; use the GitHub CLI to search for and edit those issues when you need epic details or updates (e.g., `gh issue list`, `gh issue view`, `gh issue edit`).

## Roslyn Code Navigator (MCP) – How to use

- Primary tools: `TestSolution`, `BuildSolution`, `SearchSymbols`, `FindReferences`, `GetSymbolInfo`, `ListProjects`, `AnalyzeDependencies`.
- Always pass the absolute `/mnt/.../QuickMemoryServer.sln` path from WSL. The server uses Windows SDKs by default.
- Run tests via MCP for speed/consistency: `TestSolution(solutionPath="/mnt/e/sandbox/quick-memory-server/QuickMemoryServer.sln", configuration="Release")`.
- If builds/tests fail, inspect `standardOutput`; nullable warnings in `Program.cs` are known noise unless specified otherwise.
- Symbol spelunking recipe: `SearchSymbols(symbolName="*Memory*", symbolTypes="class,method")` → `FindReferences(symbolName="MemoryStore")`.
- Environment/SDK check: `ListBuildRunners` or `RoslynEnv` before builds; ensures dotnet/VS toolchains are detected.
- Prefer `BuildSolution`/`TestSolution` over local `dotnet` in WSL unless you need custom scripts; the MCP server already uses the Windows toolchain requested by this repo.
