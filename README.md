# Quick Memory Server

This repository contains the Windows-service-based MCP memory server described in `docs/spec.md`. The current codebase targets **.NET 9**, so you will need the .NET 9 SDK preview (or newer) installed to build and run locally.

## Getting Started

1. Install the .NET 9 SDK preview.
2. Copy `QuickMemoryServer.sample.toml` to `QuickMemoryServer.toml` and update the endpoint, user, and model settings for your environment.
3. From the repository root, run:
   ```bash
   dotnet build QuickMemoryServer.sln
   ```
4. To run as a console app for development:
   ```bash
   dotnet run --project src/QuickMemoryServer.Worker
   ```
5. Hit `http://localhost:5080/health` to confirm the service is listening. Use `POST /mcp/{endpoint}/ping` with header `X-Api-Key` to exercise the placeholder MCP endpoint routing.
6. Open `http://localhost:5080/` in a browser to unlock the embedded admin SPA, log in with your API key (the SPA will discover which projects you have rights to), and manage projects, entries, users, and permissions through the existing MCP/admin APIs.
6. Queue a backup via MCP (`POST /mcp/{endpoint}/backup`) or the CLI helper:
   ```bash
   dotnet run --project tools/MemoryCtl -- backup shared --mode full --api-key YOUR_KEY
   ```
7. Observe metrics at `http://localhost:5080/metrics` (Prometheus format) and tail structured logs under `logs/quick-memory-server-*.log`.
8. The runtime JSON Schema for the exposed structs lives at `http://localhost:5080/docs/schema` (ETag + short cache).
8. Optional: run k6 scenarios in `load-tests/` to stress MCP endpoints (requires `QMS_API_KEY`).

The service is pre-configured to run as a Windows service (`AddWindowsService`) when published and installed with WiX (see `docs/plan.md`).

## Repository Layout

- `docs/` – Architecture spec and implementation plan.
- `src/QuickMemoryServer.Worker/` – Worker service project.
- `layout.json` – Installer layout manifest used by the WiX packaging process.
- `QuickMemoryServer.sample.toml` – Sample configuration file.
- `AGENTS.md` – Contributor guide for day-to-day development details.
- `tools/MemoryCtl` – Lightweight CLI for invoking MCP maintenance commands (e.g., backups).
- `logs/` – Rolling structured logs (created at runtime).

## Next Steps

- Flesh out the MCP endpoints and memory storage pipeline as outlined in the plan.
- Implement ONNX-based embedding and summarization services.
- Author the WiX installer near the end of the implementation (Phase 8).
