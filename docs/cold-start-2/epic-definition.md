# Epic 2 – ColdStart MCP Tool for Projects

## Big Idea

Provide a dedicated `coldStart` MCP tool that gives agents everything
they need to get oriented on a project in a single call:

- All curated, project-local entries that are tagged as cold-start
  recipes.
- The most recent entries for the project (optionally filtered to a
  specific epic).

Instead of re-implementing the same “run `listRecentEntries`, then hunt
for cold-start notes” flow in every client, agents can call
`coldStart { endpoint, epicSlug? }` and receive a structured payload
they can summarize and act on.

## Success Criteria

- A new MCP tool `coldStart` exists and:
  - Accepts `endpoint` (project key) and optional `epicSlug`.
  - Returns:
    - `coldStartEntries` – curated cold-start entries for that project.
    - `recentEntries` – the last 20 entries, optionally filtered by
      `epicSlug`.
- Cold-start entries:
  - Live in the project’s store (or in shared/project notes).
  - Use a `category:cold-start` tag and `curationTier = "curated"` (or
    `canonical`).
  - Are easy to create/edit via the SPA.
- Documentation:
  - Explains how to mark entries as cold-start recipes.
  - Shows agents how to use `coldStart` instead of ad-hoc sequences of
    `searchEntries` and `listRecentEntries`.

## Non-Goals

- Changing the existing prompt-based cold-start flows; this tool is an
  additional convenience, not a replacement.
- Complex filtering beyond project + optional epic slug (e.g., by
  tag combinations, time windows, or kinds).
- Per-client UI wiring; this epic focuses on server behavior and
  documentation.

