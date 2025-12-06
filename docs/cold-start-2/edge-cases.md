# Edge Cases – ColdStart MCP Tool

- **Unknown endpoint**
  - `coldStart` called with an `endpoint` that does not exist.
  - Behavior: return an MCP error similar to other tools
    (`Endpoint '<key>' is not available.`).

- **No cold-start entries**
  - No entries tagged `category:cold-start` with `curationTier` of
    `curated` or `canonical`.
  - Behavior: `coldStartEntries` is an empty array; agent can propose
    creating such entries.

- **No recent entries**
  - Project exists but has no entries yet (or none match `epicSlug`).
  - Behavior: `recentEntries` is an empty array.

- **Invalid epicSlug**
  - `epicSlug` provided does not match any entries.
  - Behavior: `coldStartEntries` still populated (project-level), but
    `recentEntries` may be empty. No special error.

- **Shared vs project-local cold-start entries**
  - Some cold-start recipes might live in shared/project notes instead
    of the main project store.
  - Behavior: initial version keeps it simple and looks only in the
    requested endpoint; future epics can extend this to include shared
    stores.

- **Performance**
  - For projects with many entries, the tool must still respond quickly.
  - Behavior: `recentEntries` is capped at 20; cold-start filtering
    should use in-memory snapshots rather than re-reading from disk.

