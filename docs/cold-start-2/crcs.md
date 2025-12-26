# CRCs – ColdStart MCP Tool

## `ColdStart` MCP Tool

- **Responsibilities**
  - Accept `endpoint` and optional `epicSlug`.
  - Resolve curated cold-start entries for the project.
  - Resolve the last 20 recent entries (optionally epic-filtered).
  - Return a structured payload agents can summarize and act on.
- **Collaborators**
  - `MemoryRouter` / `MemoryStore` for snapshot access.
  - `MemoryEntry` for fields like `tags`, `curationTier`, `epicSlug`,
    and timestamps.

## `MemoryStore`

- **Responsibilities**
  - Provide fast `Snapshot()` access for filtering both cold-start and
    recent entries.
- **Collaborators**
  - `ColdStart` tool for read-only queries.

## Admin Web UI

- **Responsibilities**
  - Allow admins to tag entries as `category:cold-start` and set
    `curationTier` appropriately.
- **Collaborators**
  - Existing Entities editor (no special UI required beyond tags).
