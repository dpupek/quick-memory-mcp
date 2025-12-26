# Workflows – MCP Prompts Backed by Quick Memory Entries

## Personas

- **Admin / Operator**
  - Manages Quick Memory configuration and API keys.
  - Curates a small set of high-value prompt recipes in `qm-prompts`.
- **Agent User (developer / support engineer)**
  - Interacts with Quick Memory via MCP in Codex/ChatGPT.
  - Wants fast, consistent ways to prime the agent for new projects or
    sessions.
- **AI Agent**
  - Calls `prompts/list` and `prompts/get` on the Quick Memory MCP
    server.
  - Uses the returned templates to bootstrap conversations and store
    context via `upsertEntry`.

## Workflow 1 – Admin Curates a New Prompt

1. Admin opens the Admin Web UI and navigates to the `qm-prompts` endpoint
   (system/locked view).
2. Creates a new entry with:
   - `kind = "prompt"`.
   - Tags: `prompt-template`, `category:onboarding` (or similar).
   - `title` describing the recipe (“Cold start for PR 1.3.x”).
   - `body` containing:
     - A small `prompt-args` block with argument metadata.
     - The actual template text with `{{placeholders}}`.
3. Saves the entry; it is written to `qm-prompts` and backed up by the
   normal backup pipeline.
4. The next time an MCP client calls `prompts/list`, the new prompt
   appears.

## Workflow 2 – Agent Uses a Prompt via MCP

1. Agent connects to Quick Memory via MCP and asks:
   - `prompts/list` to discover available recipes.
2. The MCP client shows prompt names and descriptions; the agent (or
   user) chooses one, e.g. `onboarding/first-time`.
3. The MCP client calls:
   - `prompts/get` with `name = "onboarding/first-time"` and arguments
     like `projectKey = "pr-1-3-X"`.
4. Quick Memory:
   - Resolves the entry from `qm-prompts`.
   - Parses argument metadata and applies substitutions.
   - Returns a user message containing the fully-expanded prompt text.
5. The MCP client feeds that message into the model to prime the
   conversation.

## Workflow 3 – Updating a Prompt Safely

1. Admin edits an existing prompt entry in `qm-prompts`.
2. They adjust:
   - The `prompt-args` schema (e.g. add a `severity` argument).
   - The template body to mention new workflows or fields.
3. Existing MCP clients:
   - See the updated prompt description and arguments on the next
     `prompts/list`.
   - Receive updated text on `prompts/get` with the new placeholders.

## Workflow 4 – Access Control

1. Admin issues:
   - A project-scoped API key for normal memory usage (`pr-1-3-X`, etc.).
   - A broader key for operators that can access `qm-prompts` endpoints
     directly via the Admin Web UI.
2. A normal agent key:
   - Can call `prompts/*` and use the recipes.
   - Cannot list or modify `qm-prompts` entries via `entries/*`.
3. An operator key:
   - Can edit prompt entries in `qm-prompts`.
   - Can still only delete them via the admin tier, respecting
     `isPermanent` protections.
