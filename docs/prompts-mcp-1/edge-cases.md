# Edge Cases – MCP Prompts Backed by Quick Memory Entries

- **Missing prompt name**
  - `prompts/get` is called with a name that does not exist in
    `qm-prompts`.
  - Behavior: return a clear MCP error (e.g. `not-found`) rather than
    falling back to any other endpoint.

- **Malformed `prompt-args` block**
  - The entry’s argument metadata block is not valid JSON or cannot be
    parsed.
  - Behavior: log a warning and:
    - Either treat the prompt as having no arguments, or
    - Exclude it from `prompts/list` until fixed.

- **Missing required arguments**
  - `prompts/get` is called without all required arguments.
  - Behavior: return an error describing which arguments are missing;
    do not silently substitute empty strings.

- **Unused arguments**
  - Arguments are provided that the template body never references.
  - Behavior: allow them (harmless), but consider logging debug info to
    help refine templates.

- **Prompt body too large**
  - Long recipes (or multiple messages) exceed model input size in
    some clients.
  - Behavior: we may need a soft size guidance in docs and the Admin Web UI to keep
    prompt entries relatively small and focused.

- **Concurrent edits**
  - Admin updates a prompt entry while a client is using it.
  - Behavior: MCP calls always see the latest version; no versioning or
    optimistic concurrency is needed for this epic.

- **Permissions misconfiguration**
  - A project-scoped key is accidentally granted direct access to
    `qm-prompts`.
  - Behavior: the key could edit prompts through normal entry tools.
    We should:
    - Document that `qm-prompts` is system-only.
    - Optionally add validation that refuses non-admin tiers on
      `qm-prompts` for write operations.

- **Backup / restore of prompts**
  - Prompts must be included in backups and restored with other data.
  - Behavior: confirm that `qm-prompts` participates in the existing
    backup pipeline; consider adding a test entry to verify.
