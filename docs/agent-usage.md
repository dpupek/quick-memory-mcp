# Quick Memory Server MCP Usage (MCP-first)

## MCP Quickstart
- `listProjects` – discover allowed projects (endpoints) for the current API key.
- `listRecentEntries` with `{ endpoint, maxResults }` – browse latest entries in that project (no query needed).
- `searchEntries` with `{ endpoint, text, includeShared, maxResults }` – focused retrieval.
- `getEntry` / `listEntries` – fetch one or all entries in a project.
- `upsertEntry` / `patchEntry` / `deleteEntry` – mutate entries (permanent requires Admin; `entry.project` must match endpoint).
- `relatedEntries` with `{ id, maxHops }` – graph walk.
- `requestBackup` with `{ endpoint, mode }` (Admin) – queue backup.
- `health` – server health report.

Note on shared memory:

- Projects that conceptually “inherit” the shared store are marked with
  `inheritShared` in config and in the Projects blade of the admin UI.
- Whether shared entries are merged into search by default is controlled
  by `includeInSearchByDefault`; you can always override this per-call
  using the `includeShared` parameter on `searchEntries` and
  `relatedEntries`.

## First-time agent onboarding (Codex / ChatGPT)

When you connect an agent to the Quick Memory MCP server for the first
time in a repo, use this flow to “prime” it before doing real work:

1. **Verify connectivity**
   - Call `health` and check that the status is `Healthy` and that at
     least one store is listed.
   - Call `listProjects` and note the project keys you are allowed to
     use (e.g., `projectA`, `shared`, `pr-1-3-X`).
2. **Pick a default project**
   - Ask the user which project key should be treated as the
     **default endpoint** for this repo (for example, `pr-1-3-X`).
   - Remember that choice for the rest of the session and mention it in
     your summaries.
3. **Read the field guide and help resources**
   - Fetch `resource://quick-memory/help` and skim the quickstart and
     recipes.
   - Fetch `resource://quick-memory/entry-fields` and summarize the
     `MemoryEntry field reference` so you understand how `id`, `project`,
     `kind`, `tags`, `confidence`, `curationTier`, and `isPermanent`
     affect behavior.
4. **Warm up with recent entries**
   - Call `listRecentEntries { endpoint: <default>, maxResults: 20 }`
     and summarize what kinds of memories already exist for this
     project.
   - Ask the user if there are any gaps they want you to focus on
     (e.g., “tests”, “deployment”, “RichTextBox quirks”).
5. **Confirm how to record new lessons**
   - Propose a pattern for new entries (title, kind, tags, relations,
     epicSlug/epicCase) based on the existing data.
   - Ask whether they want you to keep most entries non-permanent by
     default and when `isPermanent=true` should be used.
6. **Optional: update the client’s AGENTS.md**
   - Offer to add or update the **Quick Memory Usage** block in the
     client repo’s `AGENTS.md` using the pattern below. Only proceed
     after explicit confirmation from the user.

> For Codex configuration examples (global `~/.codex/config.toml` using
> `mcp-proxy` or `mcp-remote`), see
> `docs/codex-workspace-guide.md` or
> `resource://quick-memory/codex-workspace`.

## Common Payload Shapes
- **Relations:** array of `{ "type": "ref", "targetId": "project:key" }`.
- **Source metadata:** object `{ "type": "api", "url": "https://...", "path": "...", "shard": "..." }`.
- **Browse mode:** use `listRecentEntries` or call `searchEntries` with empty text to get recent entries.
- **Entry ids:** must not contain `/` (HTTP path safety). Prefer `project:slug` or `project-key` formats. Existing prompt ids that still contain `/` (e.g., `onboarding/first-time`) should be migrated to colon or hyphen variants when encountered.

## Recipes
### Browse then search
1) `listRecentEntries { endpoint: "projectA", maxResults: 20 }` to see latest items.
2) `searchEntries { endpoint: "projectA", text: "firewall rule" }` to narrow.

### Create or update an entry
```
upsertEntry {
  endpoint: "projectA",
  entry: {
    project: "projectA",
    id: "projectA:kb-firewall",
    kind: "procedure",
    title: "Update firewall rule",
    tags: ["firewall","ops"],
    curationTier: "curated",
    body: { text: "Steps..." },
    relations: [{ type:"ref", targetId:"projectA:net-baseline" }],
    source: { type:"api", url:"https://wiki" }
  }
}
```
Notes: project must equal endpoint; permanent entries require Admin.

### Patch an entry
`patchEntry { endpoint, id, title?, tags?, curationTier?, relations?, source? }`

### Related graph
`relatedEntries { endpoint:"projectA", id:"projectA:kb-firewall", maxHops:2 }` → nodes/edges.

### Backup
`requestBackup { endpoint:"projectA", mode:"differential" }` (Admin).

### Cold start snapshot
Use `coldStart` when you want a one-shot summary of curated cold-start
recipes plus recent activity for a project:

- Project-only:
  - `coldStart { endpoint: "<project-key>" }`
- Epic-focused:
  - `coldStart { endpoint: "<project-key>", epicSlug: "<epic-slug>" }`

The response includes:

- `endpoint` – the project key you passed.
- `coldStartEntries` – entries in that project tagged
  `category:cold-start` with `curationTier` of `curated` or `canonical`.
- `recentEntries` – the last 20 entries for that project (filtered to
  `epicSlug` when provided).

Agents should summarize both lists, call out any missing cold-start
entries, and propose new entries when appropriate.

## MemoryEntry field reference

| Field | Description |
|-------|-------------|
| `schemaVersion` | Positive integer for versioning the entry format (current value `1`). Reserved for future migrations; agents normally leave this alone. |
| `id` | Stable identifier in the form `project:key`. Used as the primary key in JSONL, search results, relations, and MCP calls. If omitted during `upsertEntry`, the server generates `<project>:<guid>`, then you can use that ID for future updates/links. |
| `project` | Logical store the entry belongs to (e.g., `projectA`). Must match the endpoint/key you call; the router uses it to pick the correct `MemoryStore` and to filter cross-project results. Defaults to the endpoint if left blank. |
| `kind` | Category of memory (`note`, `fact`, `procedure`, `conversationTurn`, `timelineEvent`, `codeSnippet`, `decision`, `observation`, `question`, `task`, etc.). Used to drive UI hints and query semantics (e.g., agents can ask “only tasks” or “only timeline events”). |
| `title` | Short human-readable label. Shown in SPA tables, search results, and graph visualizations; if empty the UI falls back to `id`, which is much harder to scan. |
| `body` | The actual content of the memory. Can be plain text or a JSON object. When JSON, try to keep a consistent shape per `kind` (`steps` for procedures, `snippet` for code, etc.) so agents can parse/augment it safely. Changes to `body` drive embedding recomputation and can change search ranking. |
| `bodyTypeHint` | Optional hint about how to interpret/render `body`. This is not used for validation; it exists to help editors/agents pick a syntax mode. Recommended values: `text`, `json`, `markdown`, `html`, `xml`, `yaml`, `toml`, `csv`. |
| `tags` | Free-form labels used for faceted search (“only entries tagged `backup`”). The search engine indexes these separately, and the SPA tags editor exposes them directly. Use them for coarse-grained grouping across kinds (`ops`, `design`, `decision`). |
| `keywords` | Optional, usually system- or tool-generated keywords (e.g., extracted phrases). They don’t drive any special logic today beyond search, so agents can ignore or populate them as needed. |
| `relations` | Array of `{ type, targetId, weight? }` describing graph edges to other entries. `targetId` must be a valid `project:key`. `relatedEntries` and some UI flows use this to hop the knowledge graph (e.g., “see-also”, “dependency”). Use it when you want agents to follow curated connections instead of guessing links from text. |
| `source` | `{ type, url, path, shard }` metadata pointing back to where this memory came from: HTTP API, local file, shard ID, etc. Useful for audit/troubleshooting (e.g., “regenerate from this path”) and for agents deciding whether a memory is authoritative or derived. |
| `embedding` | Semantic vector backing hybrid search. If omitted, the server computes it from `body`. You only care about this field if you’re doing offline imports or using your own embedding model; otherwise, let the server manage it. Length must match `global.embeddingDims`. |
| `timestamps` | `{ createdUtc, updatedUtc, sourceUtc? }` used for recency bias and `listRecentEntries`. If unset, the server fills `createdUtc` / `updatedUtc` with “now”. Agents can use these fields to prioritize fresh memories or filter out stale ones. |
| `ttlUtc` | Optional expiration time (“soft delete” after a given instant). The loader can ignore entries past TTL, which is useful for temporary experiments or noisy telemetry. This is ignored when `isPermanent = true`. |
| `confidence` | A 0–1 score representing how trustworthy the memory is (e.g., from your ingestion pipeline). Higher confidence can bias ranking and help agents choose between conflicting memories. Defaults to `0.5` if omitted; agents may use it to down-rank low-confidence facts instead of deleting them. |
| `curationTier` | Editorial status: `provisional` (default, unreviewed), `curated` (reviewed and solid), or `canonical` (the source of truth). The server enforces the enum and uses it in ranking; UI badges and some workflows highlight canonical entries. Admin tier is required to make permanent entries canonical. |
| `epicSlug` / `epicCase` | Optional linkage to your planning system (epics, FogBugz/Jira cases, etc.). Use these when you want agents to reason over “everything attached to epic X” without relying purely on tags. |
| `isPermanent` | Hard guardrail that prevents deletion unless `force = true` and the caller is Admin. Use it for decisions, contracts, or safety-critical facts you never want casually removed. Defaults to `false`. |
| `pinned` | Soft flag that UIs and agents can use to emphasize important entries in lists or summaries (e.g., “show pinned items first”). Does not change search semantics by itself but is intended for UX/agent sorting hints. |

**Notes**
- `upsertEntry` enforces `project` equality and fills `id`, `timestamps`, and `curationTier` defaults automatically.
- `patchEntry` accepts the same fields but only replaces those present; relations/source payloads must remain valid JSON objects/arrays.
- Permanent entries can only be deleted with `deleteEntry(..., force: true)` by Admin-tier keys.

## Prompt templates via MCP (prompts-repository)

Quick Memory exposes curated prompt templates through the MCP
`prompts` capability, backed by entries in a dedicated
`prompts-repository` endpoint.

- Prompt entries:
  - Live in `prompts-repository` with `kind = "prompt"`.
  - Include a `prompt-template` tag plus one or more `category:*` tags
    (e.g., `category:onboarding`, `category:cold-start`).
  - Use a stable `id` that becomes the MCP `prompt.name`
    (e.g., `onboarding/first-time`).
- Argument metadata:
  - Defined in the entry `body` using a `prompt-args` JSON block at the
    top of the markdown, for example:

    ```markdown
    ```prompt-args
    [
      { "name": "projectKey", "description": "Quick Memory endpoint key", "required": true }
    ]
    ```

    Use Quick Memory for project {{projectKey}}…
    ```

  - The server parses this block for `prompts/list` and strips it
    before the template text is sent to the model.
- Placeholder syntax:
  - Template bodies use `{{argName}}` where `argName` matches an
    argument `"name"` in the `prompt-args` block.
  - `prompts/get` performs substitution using the provided arguments and
    returns a `messages[]` payload suitable for priming the agent.

Agents should prefer `prompts/list` + `prompts/get` to fetch these
recipes instead of copying prompt text from static docs.

Recommended flow for agents:

- For **first-time** usage on a repo:
  - Call the prompt tool equivalent of `onboarding/first-time` with
    `projectKey = "<default-endpoint>"` to bootstrap your behavior.
- For **cold starts**:
  - Use the `cold-start:project` prompt with the same `projectKey` at
    the beginning of a new session.
- After **investigations**:
  - Use `lessons/new-entry` or `investigation/troubleshoot` to draft
    `upsertEntry` payloads and confirm them with the user before
    sending.
- For **AGENTS.md guidance**:
  - Use `onboarding/agents-guidance` to propose a `Quick Memory Usage`
    section for the client repo, then follow the confirmation rules in
    the section below.

## Resources
- `resource://quick-memory/help` – main MCP help + quickstart.
- `resource://quick-memory/end-user-help` – end-user guide.
- `resource://quick-memory/cheatsheet` – condensed “do X → call Y”.
- `resource://quick-memory/entry-fields` – MemoryEntry field reference (IDs, kinds, body, tags, relations, source, timestamps, TTL, tiers). The same table appears in `/admin/help/agent`.

## Tips
- If auth fails, re-enter the API key; sessions persist in the SPA.
- Keys/slugs must match `^[A-Za-z0-9_-]+$`; storage defaults to `global.storageBasePath` + key if omitted.
- Use `listRecentEntries` for cold-start browsing; then `searchEntries` for precision.

## Recommending client-side AGENTS.md guidance

When you’re acting as an MCP-aware agent inside a client (Codex, ChatGPT, etc.), you should **offer** to add a short Quick Memory section to the project’s `AGENTS.md` (or equivalent) so future agents inherit the same rules.

Suggested pattern (adjust names/IDs per project):

```markdown
## Quick Memory Usage

* **Project selection:** Premier Responder 1.3.x work should be logged under the `pr-1-3-X` endpoint (use `listProjects` if you forget the slug). Reserve `nc-7-x` or other projects for their respective branches; don’t cross-post.
* **Common flow:** `listRecentEntries { endpoint:\"pr-1-3-X\", maxResults:20 }` to catch up, `searchEntries` for focused lookups, then `upsertEntry`/`patchEntry` to capture new lessons. Include concise titles, tags like `rtf`, `hyperlinks`, `tests`, and reference any relevant FogBugz/case numbers.
* **Cold starts:** at the beginning of every session (especially if you weren’t the previous agent), run `listRecentEntries` before doing any work so you inherit the newest lessons and avoid duplicating investigations.
* **What to record:** build/test recipes that differ from the norms, control quirks, parser behaviors, stakeholder decisions, and troubleshooting guidance future agents need. Prefer facts/notes over raw logs; attach summaries instead of pasting entire transcripts.
* **Operational tips:** if a Quick Memory command times out (e.g., idle connection), restart the chat/session to reset the MCP client, then retry. You can also re-run `listProjects` to confirm connectivity before another `upsertEntry`.
* **Security:** the endpoint enforces per-project access. Keep entries non-permanent unless the user explicitly requests archival, and avoid storing credentials or customer PHI.
```

Agent behavior:
- Ask the user:  
  - which Quick Memory endpoint should be treated as the **default project** for this repo (e.g., `pr-1-3-X`), and  
  - whether they want you to add/update the `Quick Memory Usage` block in `AGENTS.md`.
- Only propose edits; do not silently change the client’s repo. Once confirmed, update `AGENTS.md` with the project-specific endpoint, examples, and any house rules the user calls out.
