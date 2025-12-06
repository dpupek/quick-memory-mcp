# Epic 2 – ColdStart MCP Tool Roadmap

## Phase 0 – Design

- [ ] Confirm shape of `coldStart` response:
  - [ ] `endpoint` (string).
  - [ ] `coldStartEntries` (array of `MemoryEntry`).
  - [ ] `recentEntries` (array of `MemoryEntry`).
- [ ] Decide whether to include shared or only project-local entries for
      cold-start recipes (initially project-local only).

## Phase 1 – Tool Implementation

- [ ] Add a `coldStart` MCP tool to `MemoryMcpTools`:
  - [ ] Accept `endpoint` and optional `epicSlug`.
  - [ ] Resolve the `MemoryStore` for `endpoint`.
  - [ ] Filter `Snapshot()` results for:
    - [ ] `coldStartEntries`: `tags` contains `category:cold-start` and
          `curationTier` ∈ {`curated`, `canonical`}.
    - [ ] `recentEntries`: top 20 by `timestamps.updatedUtc` /
          `createdUtc`, filtered by `epicSlug` when provided.
  - [ ] Return a new response type capturing both lists.

## Phase 2 – Docs & Agent Guidance

- [ ] Update `docs/agent-usage.md`:
  - [ ] Add a section explaining when and how to call `coldStart`.
  - [ ] Show example usage for project-only and epic-specific cold starts.
- [ ] Update `docs/spec.md` to mention the `coldStart` tool and how it
      relates to `listRecentEntries`.

## Phase 3 – SPA & UX (Optional)

- [ ] Add a small “Cold start preview” to the Entities tab:
  - [ ] A button that calls `coldStart` for the selected project.
  - [ ] Displays cold-start entries and recent items in a side panel.

## Phase 4 – Hardening

- [ ] Add tests around:
  - [ ] Filtering by `category:cold-start`.
  - [ ] Filtering recent entries by `epicSlug`.
  - [ ] Behavior when there are no cold-start entries.
- [ ] Verify performance on large projects (snapshot-only, no extra IO).

