# CRCs – MCP Prompts Backed by Quick Memory Entries

## `qm-prompts` Endpoint (Configuration)

- **Responsibilities**
  - Define a dedicated, system-level endpoint for curated prompts.
  - Ensure the storage path participates in normal backups.
  - Mark the endpoint as locked/system so the SPA and APIs treat it as
    read-only for non-admin tiers.
- **Collaborators**
  - `ServerOptions` / endpoint configuration binding.
  - `MemoryStoreFactory` / `MemoryRouter` for store resolution.

## `MemoryStore` / `MemoryRouter`

- **Responsibilities**
  - Resolve the `qm-prompts` store like any other endpoint.
  - Provide read access for MCP `prompts/*` implementations.
- **Collaborators**
  - `JsonlRepository` for storing prompt entries.
  - MCP prompts handlers (new component) for lookup.

## MCP Prompts Handler

- **Responsibilities**
  - Implement `prompts/list` and `prompts/get` on top of
    `MemoryEntry` data from `qm-prompts`.
  - Parse and validate the `prompt-args` metadata block.
  - Perform argument substitution on template bodies.
- **Collaborators**
  - `MemoryRouter` / `MemoryStore` for reading prompt entries.
  - `MemoryEntry` model for fields (id, title, body, tags, etc.).

## Admin SPA (Prompts Authoring)

- **Responsibilities**
  - Provide a way for admins to view and edit prompt entries in
    `qm-prompts`.
  - Guide authors to use the `prompt-args` block and tags for
    categories.
- **Collaborators**
  - Admin endpoints for listing/editing entries.
  - Help docs that describe prompt authoring conventions.

