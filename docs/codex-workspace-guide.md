# Workspace-Level Codex MCP Configuration Guide

This guide explains how to add a **workspace-local `.codex`
configuration**, safely manage **API keys**, configure **timeouts**, and
ensure everything stays **out of version control**.

------------------------------------------------------------------------

## 1. Creating a Workspace-Local `.codex` Directory

Codex supports configuration overrides on a per-project basis by placing
a `config.toml` file inside:

    ./.codex/config.toml

### Steps:

``` bash
cd /your-project
mkdir -p .codex
touch .codex/config.toml
```

------------------------------------------------------------------------

## 2. Example `config.toml` for a Project-Specific MCP Server

Place this inside `.codex/config.toml`:

``` toml
[mcp_servers.quick-memory]
command = "npx"
args = [
  "mcp-remote@latest",
  "http://localhost:5080/mcp",
  "--header", "X-Api-Key:${QUICK_MEMORY_API_KEY}",
  "--allow-http",
  "--debug"
]

# Timeouts in milliseconds
timeout_ms = 60000
startup_timeout_ms = 60000
```

### Notes:

-   `${QUICK_MEMORY_API_KEY}` is looked up from your environment
    variables.
-   This configuration applies **only to the project folder it lives
    in**.
-   It fully overrides any global MCP server with the same name.

------------------------------------------------------------------------

## 3. Adding `.codex` to `.gitignore`

You do **not** want `.codex` (or any API keys) committed to version
control.

In the project root, edit `.gitignore`:

``` bash
touch .gitignore
```

Add:

``` gitignore
# Codex workspace-specific configuration
.codex/
```

This prevents accidental commits of MCP settings and secrets.

------------------------------------------------------------------------

## 4. Creating a Project-Limited API Key

To avoid cross-project access, create a **least-privilege** service
account dedicated only to this project.

### Requirements for this account:

-   Only permitted to access the specific project's memory records.
-   No cross-project read or write permissions.
-   No administrative privileges.
-   Generates its own API key.

### Example environment variable setup:

**macOS / Linux (bash or zsh):**

In `~/.bashrc` or `~/.zshrc`:

``` bash
export QUICK_MEMORY_API_KEY="your-project-limited-api-key"
```

Reload:

``` bash
source ~/.zshrc
```

**Windows PowerShell:**

``` powershell
[System.Environment]::SetEnvironmentVariable(
  "QUICK_MEMORY_API_KEY",
  "your-project-limited-api-key",
  "User"
)
```

Restart VS Code so Codex picks up the new variable.

------------------------------------------------------------------------

## 5. Directory Structure Example

Your workspace might look like:

    /your-project
      .codex/
        config.toml
      src/
      README.md
      .gitignore

Codex automatically merges this configuration on top of your global one
at `~/.codex/config.toml`.

------------------------------------------------------------------------

## 6. How Codex Uses This Configuration

When you open the workspace:

1.  Codex loads global config from `~/.codex/config.toml`.
2.  It loads `./.codex/config.toml`.
3.  Servers defined in the project config override global ones of the
    same name.
4.  API key variables are substituted from the environment.
5.  MCP tools become available **only inside this project**.

This is the correct way to isolate access and behavior per repository.

------------------------------------------------------------------------

## 7. Quick Checklist

-   [x] Create `.codex/` folder in the project root\
-   [x] Add project-specific `config.toml`\
-   [x] Add `.codex/` to `.gitignore`\
-   [x] Create a project-limited service account\
-   [x] Store API key as `QUICK_MEMORY_API_KEY` environment variable\
-   [x] Restart VS Code\
-   [x] Use project-specific MCP tools safely and securely

------------------------------------------------------------------------

If you need a **template repo**, **multi-environment setup**, or
**automated API key provisioning**, I can generate those too.
