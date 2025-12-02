# Repository Guidelines

## Project Structure & Module Organization
- The worker service lives in `src/QuickMemoryServer.Worker/`; match new code to existing folders (`Configuration/`, `Memory/`, `Models/`, `Services/`).
- `docs/` tracks the spec, the evolving `plan.md`, usage recipes, and schema/agent guides; update them before touching runtime behavior.
- Runtime aids and tooling are under `tools/` (backup CLI, watchers) and `load-tests/` (k6 scripts); platform-specific installer layout lives in `layout.json`.

## Build, Test, and Development Commands
- Set `NEXPORT_WINDOTNET` to `"/mnt/c/Program Files/dotnet/"` once per shell so scripts use `"$NEXPORT_WINDOTNETdotnet.exe" build ...`, `test`, and `run` (same binary for CLI, restore, publish).
- For git operations under WSL, prefer `"/mnt/c/Program Files/Git/bin/git.exe" status` (note timeline slowdowns on native Linux Git).
- `"$NEXPORT_WINDOTNETdotnet.exe" build QuickMemoryServer.sln` compiles everything with the required .NET 9 preview.
- `"$NEXPORT_WINDOTNETdotnet.exe" test` runs all xUnit suites; `run --project src/QuickMemoryServer.Worker` launches the worker for manual validation (watch logs for `Listening on http://localhost:5080`).

## Coding Style & Naming Conventions
- C# 12, file-scoped namespaces, nullable enabled, `var` for obvious declarations, PascalCase for types/methods, camelCase for locals/parameters; `*Service` suffix on services.
- Keep new files ASCII; mention new localizable strings when adding user-facing copy.
- Prefer DI-based services over static singletons; option/config classes live under `Configuration/`.

## Testing Guidelines
- Tests use xUnit + FluentAssertions; keep naming `{MethodUnderTest}_{Scenario}_{ExpectedOutcome}`.
- Add coverage for persistence, search, MCP mappings, and file watchers; include canonical/permanent edge cases.
- Run `"$NEXPORT_WINDOTNETdotnet.exe" test` before pairing changes; document skipped tests with TODO comments referencing follow-up cases.

## Commit & Pull Request Guidelines
- Conventional commits referencing the current case number (e.g., `case 202345 feat(memory): add streamable mcp transport`).
- PRs must describe what changed, reference spec sections (`docs/spec.md`), include tests run, link the relevant `docs/plan.md` iteration, and attach installer/log artifacts when applicable.
- Always ask for a case number before committing; do **not** auto-commit.

## Documentation & Planning Discipline
- Update `docs/plan.md` with checkboxes/phases for every new epic; call out completed phases (0–7) and the next focus (streamable MCP implementation + schema). Link decisions/questions directly in the plan file.
- Keep `docs/spec.md` synchronized with runtime changes (memory kinds, curation tiers, embedded schema URLs) and document observations/recipes in `docs/agent-usage.md`.
- Regenerate references to `agent-usage` when introducing new MCP tools or recipes (search, project checks, entry change detection).

## MCP & Observability Conventions
- Prefer the Model Context Protocol C# SDK (`ModelContextProtocol` NuGet) for the Streamable HTTP transport; expose `MapMcp` endpoints that reuse existing services and describe tooling via `describe`/`getUsageDoc`.
- Monitor MCP ops via Serilog/EventCounters (`qms_*` metrics) and expose `/health` plus Prometheus instrumentation.
- Use the Roslyn MCP help resource (`resource://roslyn/help`) and the `roslyn_code_navigator` tools (`SearchSymbols`, `FindReferences`, etc.) for complex lookups.
- Document new MCP commands, config samples (TOML + `.codex/config.toml`), and Tier matrices before shipping.
