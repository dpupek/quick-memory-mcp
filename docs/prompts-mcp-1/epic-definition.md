# Epic 1 – MCP Prompts Backed by Quick Memory Entries

## Big Idea

Expose curated, argument-driven MCP prompts using Quick Memory itself as
the backing store. Instead of hard-coding prompt templates in code, we
store them as `MemoryEntry` records in a protected `qm-prompts`
endpoint and surface them via the MCP `prompts/list` and `prompts/get`
capabilities.

Agents should be able to:

- Discover high-value recipes (first-time onboarding, cold start,
  recording lessons, investigations) through `prompts/list`.
- Request a specific recipe by name with arguments via `prompts/get`
  and receive a ready-to-send message payload.
- Rely on admins to evolve prompts over time using the existing SPA and
  backup mechanisms, without changing binaries.

## Success Criteria

- A dedicated `qm-prompts` endpoint exists and is:
  - Backed by the usual JSONL store.
  - Marked as a system/locked endpoint in config.
  - Not editable or deletable by normal project-scoped API keys.
- Curated prompt entries:
  - Use `kind = "prompt"` and a `prompt-template` tag.
  - Live in the `qm-prompts` endpoint and are marked `isPermanent`.
  - Use a documented placeholder syntax (e.g. `{{projectKey}}`) plus a
    small in-body schema for arguments.
- MCP `prompts/list`:
  - Returns only curated prompts from `qm-prompts`.
  - Includes argument metadata derived from the entry’s schema block.
  - Supports the spec’s cursor shape, even if we initially return
    everything in a single page.
- MCP `prompts/get`:
  - Resolves a prompt by name from `qm-prompts`.
  - Applies argument substitution safely.
  - Returns a `messages[]` payload that agents can send directly.
- Documentation:
  - Explains how to author, categorize, and maintain prompt entries in
    the SPA.
  - Encourages agents to use `prompts/*` instead of copy-pasting
    recipes from static docs.

## Non-Goals

- Generic “treat every note as a prompt” behavior; only curated entries
  in `qm-prompts` are exposed via `prompts/*`.
- A full-blown DSL for prompt authoring; we only need a minimal,
  documented argument schema and placeholder syntax.
- Fine-grained per-prompt ACLs beyond the existing endpoint + tier model
  (project-scoped keys never see `qm-prompts` directly).

## Prompt Entry Shape (Prompts Repository)

All curated prompts live in the `prompts-repository` endpoint and use a
consistent shape:

- `project` – must be `prompts-repository`.
- `kind` – must be `"prompt"` to be eligible for `prompts/*`.
- `tags` – must include:
  - `prompt-template` (marks the entry as a prompt), and
  - One or more category tags, e.g.:
    - `category:onboarding`
    - `category:cold-start`
    - `category:lesson`
    - `category:troubleshooting`
- `isPermanent` – should be `true` for all curated prompts; deletions
  require admin + `force`.
- `title` – human-readable name for the recipe; surfaced as the MCP
  prompt description.
- `id` – stable slug used as the MCP `prompt.name`, e.g.:
  - `onboarding/first-time`
  - `cold-start/project`
  - `lessons/new-entry`

### Argument Metadata (`prompt-args` Block)

Prompt arguments are defined inside the entry `body` using a small JSON
block at the top:

```markdown
```prompt-args
[
  { "name": "projectKey", "description": "Quick Memory endpoint key", "required": true },
  { "name": "topic", "description": "Area to investigate", "required": false }
]
```

Use Quick Memory for project {{projectKey}}. Focus on {{topic}}…
```

Rules:

- The `prompt-args` block:
  - Must be valid JSON array of objects.
  - Each object must contain at least `"name"` and `"description"`.
  - `"required": true|false` is optional; default is `false`.
- The block is stripped from the body before the template is returned
  to the model.
- MCP `prompts/list` parses this block into `arguments[]` for each
  prompt.

### Placeholder Syntax in Body

- Placeholders use double curly braces: `{{argName}}`.
- `argName` must match one of the `"name"` values defined in the
  `prompt-args` block.
- When `prompts/get` is called:
  - Required arguments must be provided; missing ones cause a clear
    error (no silent empty substitutions).
  - Provided values are substituted into the template body wherever
    `{{argName}}` appears.
- Extra arguments (not referenced in the body) are allowed but ignored;
  they may be useful if we extend templates later.
