# Epic 1 – MCP Prompts Roadmap

## Phase 0 – Design & Conventions

- [x] Decide on the system endpoint key/slug (`qm-prompts` /
      `prompts-repository`) and mark it as locked/system in config.
- [x] Document the required entry shape:
  - [x] `kind = "prompt"`, tag `prompt-template`.
  - [x] Category tags (e.g. `category:onboarding`, `category:cold-start`).
  - [x] `prompt-args` metadata block and placeholder syntax
        (`{{argName}}`).
- [x] Confirm that `qm-prompts` participates in backup/restore like
      other endpoints.

## Phase 1 – Backend Storage & Access

- [x] Add `qm-prompts` endpoint definition to `ServerOptions` /
      `QuickMemoryServer.toml`.
- [x] Ensure `MemoryStoreFactory` / `MemoryRouter` resolve
      `qm-prompts` correctly.
- [x] Enforce that non-admin tiers cannot write to `qm-prompts` through
      existing entry tools (or at least document that it is system-only).

## Phase 2 – MCP Prompts Capability

- [x] Implement `prompts/list`:
  - [x] Query `qm-prompts` for entries with `kind = "prompt"` and
        `prompt-template` tag.
  - [x] Parse `prompt-args` metadata into MCP `arguments[]`.
  - [x] Return the spec-compliant list shape and accept an optional
        `cursor` for future paging.
- [x] Implement `prompts/get`:
  - [x] Resolve a prompt by `name` (mapped from entry id/slug).
  - [x] Validate required arguments; return clear errors when missing.
  - [x] Apply placeholder substitution and return `messages[]` with a
        user text message.

## Phase 3 – Seed Prompts & Authoring UX

- [x] Seed `qm-prompts` with core recipes:
  - [x] First-time onboarding.
  - [x] Cold start.
  - [x] Recording a new lesson.
  - [x] Investigation / troubleshooting flows.
- [x] Update the Admin SPA to:
  - [x] Show `qm-prompts` in a system/locked view.
  - [x] Provide minimal helpers/snippets for `prompt-args` blocks and
        category tags.

## Phase 4 – Docs & Agent Guidance

- [x] Update `docs/agent-usage.md` to describe:
  - [x] How agents should use `prompts/list` and `prompts/get`.
  - [x] How prompt categories map to workflows (onboarding, cold start,
        lessons, etc.).
- [x] Update `docs/end-user-help.md` / help resources to:
  - [x] Explain `qm-prompts` at a high level.
  - [x] Encourage admins to manage recipes there instead of only in
        static docs.

## Phase 5 – Hardening & Future Enhancements

- [x] Add tests around malformed `prompt-args`, missing arguments, and
      permission boundaries.
- [ ] Consider richer multi-message prompts (system + user messages)
      if needed.
- [ ] Evaluate whether project-specific prompts (per endpoint) are
      required and, if so, extend the design without breaking `qm-prompts`.
