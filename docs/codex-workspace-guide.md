# Codex MCP Configuration Guide (Global)

> **Updated 5 December 2025:** Codex no longer honors workspace-local
> `.codex/config.toml` overrides. All MCP servers must be declared in the
> global file at `~/.codex/config.toml`. Use per-project API keys and
> separate server blocks to constrain access instead of per-repo
> configuration files.

This guide explains how to update the global Codex configuration, wire it
up to the Quick Memory MCP server via either `mcp-proxy` or
`mcp-remote`, and keep API keys isolated per project.

----------------------------------------------------------------------------

## 1. Find the global config

Codex reads one config file:

```
~/.codex/config.toml
```

Create it if it does not exist. All examples below go into that file.

----------------------------------------------------------------------------

## 2. Recommended: `mcp-proxy` (no Node, supports headers)

```toml
[mcp_servers.quick-memory]
command = "mcp-proxy"
args = [
  "http://localhost:5080/mcp",
  "--transport", "streamablehttp",
  "--no-verify-ssl",
  "--headers", "X-Api-Key", "${QM_AUTH_TOKEN}",
  "--stateless"
]
timeout_ms = 60000
startup_timeout_ms = 60000
```

Notes:

- `mcp-proxy` is Python-based and skips the OAuth dance, so it works well
  inside WSL. Install it once via `uv tool install mcp-proxy` **from a WSL
  terminal** so the binary lands in your Linux path (or run through
  `uvx mcp-proxy …`).
- Use `--headers` for multiple headers if needed (e.g., `Authorization`).
- `--transport streamablehttp` enables schema streaming; remove it if you
  only need classic HTTP.
- `--stateless` is recommended for Quick Memory because auth is per
  request; it also behaves better when multiple clients on the same
  workstation share the same server.
- `--debug` is available if you need verbose bridge logging; it is a
  flag only (do **not** append `true` or any other value).

If you prefer to keep keys in a small Codex-specific env table instead
of your shell profile, you can also write:

```toml
[mcp_servers.quick-memory]
command = "mcp-proxy"
args = [
  "http://localhost:5080/mcp",
  "--headers", "X-Api-Key", "${QM_AUTH_TOKEN}",
  "--stateless"
]
env = {"QM_AUTH_TOKEN"="/K/XodEPueCMorpZV8qKP47svleB0FQ9jmMVtIXO+Lw="}
```

The `env` table must be on a single line and both the variable name and
value must be quoted.

----------------------------------------------------------------------------

## 3. Alternative: `mcp-remote` (Node bridge)

```toml
[mcp_servers.quick-memory]
command = "npx"
args = [
  "mcp-remote@latest",
  "http://localhost:5080/mcp",
  "--header", "X-Api-Key:/K/XodEPueCMorpZV8qKP47svleB0FQ9jmMVtIXO+Lw=",
  "--allow-http",
  "--debug"
]
```

Tips:

- The bridge still expects an OAuth-capable keyring; if your
  environment lacks one (common inside WSL), prefer `mcp-proxy` instead
  of `mcp-remote`.
- If another process is already bound to the bridge port, pass
  `"--port","9100"` (or any free port) and update Codex accordingly.
- Cache files live in `~/.mcp-auth/mcp-remote-*/`; delete them when keys
  rotate.

----------------------------------------------------------------------------

## 4. Handling multiple projects without workspace configs (mcp-remote)

Because Codex only reads the global config, create **multiple server
entries**—one per project key—and give them unique names:

```toml
[mcp_servers.quick-memory-pr1]
command = "mcp-proxy"
args = [
  "http://localhost:5080/mcp",
  "--headers", "X-Api-Key", "${PR1_KEY}",
  "--stateless"
]

[mcp_servers.quick-memory-shared]
command = "mcp-proxy"
args = [
  "http://localhost:5080/mcp",
  "--headers", "X-Api-Key", "${SHARED_KEY}",
  "--stateless"
]
```

Workflow:

1. Issue a project-limited API key in the Admin Web UI (Users / Project Permissions).
2. Export the key in your shell profile (or use a password manager that
   can inject environment variables):
   
   ```bash
   export PR1_KEY="project-1-api-key"
   export SHARED_KEY="shared-readonly-api-key"
   ```
3. Toggle which server Codex uses by selecting it inside the UI, or keep
   them all enabled if you routinely work across projects.
4. When rotating keys, update the environment variables and restart
   Codex; no repo-local edits are needed.

----------------------------------------------------------------------------

## 5. Keeping secrets out of version control

- The config lives in your home directory, so it is already outside your
  Git repos. Do **not** copy it into individual projects.
- Prefer environment variables for API keys (`${PR1_KEY}`) so the
  global file never needs to contain raw secrets.
- If you must keep plaintext keys, ensure your workstation disk is
  encrypted and rotate keys frequently from the Admin Web UI.

----------------------------------------------------------------------------

## 6. Quick checklist

- [ ] Update `~/.codex/config.toml` with either the `mcp-proxy` or  `mcp-remote` block.
- [ ] Create/rotate project-scoped API keys in the Admin Web UI.
- [ ] Export the API keys as environment variables before launching Codex (or use a credential manager).
- [ ] Restart Codex so it picks up the new configuration.
- [ ] Confirm `listProjects` shows only the endpoints your key should access.

Need help automating key rotation or managing many projects? Ask and we
can script the bridge startup or generate shell snippets for you.
