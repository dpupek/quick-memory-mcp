# Workflows – ColdStart MCP Tool

## Workflow 1 – Agent Cold Start on a Project

1. Agent identifies the default project key (e.g., `projectA` or
   `pr-1-3-X`) for the current repo.
2. Agent calls:
   - `coldStart { endpoint: "<projectKey>" }`.
3. Server returns:
   - `coldStartEntries` – curated recipes for getting oriented.
   - `recentEntries` – last 20 updated entries for that project.
4. Agent:
   - Summarizes the cold-start recipes and recent work.
   - Points out any gaps (e.g., no cold-start entries defined, sparse
     history).
5. Agent asks the user which cold-start recipe to follow first and
   proceeds accordingly.

## Workflow 2 – Epic-Focused Cold Start

1. User specifies an epic slug they care about (e.g., `epicSlug = "ui-1-3-x"`).
2. Agent calls:
   - `coldStart { endpoint: "<projectKey>", epicSlug: "<slug>" }`.
3. Server:
   - Returns the same `coldStartEntries` as before (project-level).
   - Filters `recentEntries` to those with matching `epicSlug`.
4. Agent:
   - Summarizes recent work specifically for that epic.
   - Uses cold-start entries as context for what to do next.

## Workflow 3 – Creating Cold-Start Entries

1. Admin or power user identifies one or more entries that should serve
   as cold-start recipes (e.g., “How to quickly reorient on PR 1.3.x”).
2. In the Admin Web UI, they:
   - Edit the entry.
   - Add the tag `category:cold-start`.
   - Ensure `curationTier` is at least `curated`.
3. On the next `coldStart` call for that endpoint, the entry appears
   in `coldStartEntries`.

## Workflow 4 – Handling Missing Cold-Start Entries

1. Agent calls `coldStart` for a project that has no curated
   cold-start entries.
2. Server returns:
   - An empty `coldStartEntries` list.
   - The usual `recentEntries` data.
3. Agent:
   - Explicitly calls out that no cold-start recipes are defined.
   - Proposes creating a new entry (or prompt) that can be tagged
     `category:cold-start` for future use.
