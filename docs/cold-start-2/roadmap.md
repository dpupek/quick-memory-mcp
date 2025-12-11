# Epic 2 – ColdStart MCP Tool Roadmap

## Phase 0 – Design

- [x] Confirm shape of `coldStart` response:
  - [x] `endpoint` (string).
  - [x] `coldStartEntries` (array of `MemoryEntry`).
  - [x] `recentEntries` (array of `MemoryEntry`).
- [x] Decide whether to include shared or only project-local entries for
      cold-start recipes (initially project-local only, now honoring
      the project’s `includeInSearchByDefault` for shared).

## Phase 1 – Tool Implementation

- [x] Add a `coldStart` MCP tool to `MemoryMcpTools`:
  - [x] Accept `endpoint` and optional `epicSlug`.
  - [x] Resolve the `MemoryStore` for `endpoint`.
  - [x] Filter `Snapshot()` results for:
    - [x] `coldStartEntries`: `tags` contains `category:cold-start` and
          `curationTier` ∈ {`curated`, `canonical`}, optionally enriched
          from the `shared` project when `includeInSearchByDefault` is true.
    - [x] `recentEntries`: top 20 by `timestamps.updatedUtc` /
          `createdUtc`, filtered by `epicSlug` when provided.
  - [x] Return a new response type capturing both lists and a
        `recentAllSlugsLast24hCount` hint for recent activity.

## Phase 2 – Docs & Agent Guidance

- [x] Update `docs/agent-usage.md`:
  - [x] Add a section explaining when and how to call `coldStart`.
  - [x] Show example usage for project-only and epic-specific cold starts.
- [x] Update `docs/spec.md` to mention the `coldStart` tool and how it
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
