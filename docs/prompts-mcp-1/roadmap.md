# Epic 1 – MCP Prompts Roadmap

## Phase 0 – Design & Conventions

- [ ] Decide on the system endpoint key/slug (`qm-prompts` /
      `prompts-repository`) and mark it as locked/system in config.
- [ ] Document the required entry shape:
  - [ ] `kind = "prompt"`, tag `prompt-template`.
  - [ ] Category tags (e.g. `category:onboarding`, `category:cold-start`).
  - [ ] `prompt-args` metadata block and placeholder syntax
        (`{{argName}}`).
- [ ] Confirm that `qm-prompts` participates in backup/restore like
      other endpoints.

## Phase 1 – Backend Storage & Access

- [ ] Add `qm-prompts` endpoint definition to `ServerOptions` /
      `QuickMemoryServer.toml`.
- [ ] Ensure `MemoryStoreFactory` / `MemoryRouter` resolve
      `qm-prompts` correctly.
- [ ] Enforce that non-admin tiers cannot write to `qm-prompts` through
      existing entry tools (or at least document that it is system-only).

## Phase 2 – MCP Prompts Capability

- [ ] Implement `prompts/list`:
  - [ ] Query `qm-prompts` for entries with `kind = "prompt"` and
        `prompt-template` tag.
  - [ ] Parse `prompt-args` metadata into MCP `arguments[]`.
  - [ ] Return the spec-compliant list shape and accept an optional
        `cursor` for future paging.
- [ ] Implement `prompts/get`:
  - [ ] Resolve a prompt by `name` (mapped from entry id/slug).
  - [ ] Validate required arguments; return clear errors when missing.
  - [ ] Apply placeholder substitution and return `messages[]` with a
        user text message.

## Phase 3 – Seed Prompts & Authoring UX

- [ ] Seed `qm-prompts` with core recipes:
  - [ ] First-time onboarding.
  - [ ] Cold start.
  - [ ] Recording a new lesson.
  - [ ] Investigation / troubleshooting flows.
- [ ] Update the Admin SPA to:
  - [ ] Show `qm-prompts` in a system/locked view.
  - [ ] Provide minimal helpers/snippets for `prompt-args` blocks and
        category tags.

## Phase 4 – Docs & Agent Guidance

- [ ] Update `docs/agent-usage.md` to describe:
  - [ ] How agents should use `prompts/list` and `prompts/get`.
  - [ ] How prompt categories map to workflows (onboarding, cold start,
        lessons, etc.).
- [ ] Update `docs/end-user-help.md` / help resources to:
  - [ ] Explain `qm-prompts` at a high level.
  - [ ] Encourage admins to manage recipes there instead of only in
        static docs.

## Phase 5 – Hardening & Future Enhancements

- [ ] Add tests around malformed `prompt-args`, missing arguments, and
      permission boundaries.
- [ ] Consider richer multi-message prompts (system + user messages)
      if needed.
- [ ] Evaluate whether project-specific prompts (per endpoint) are
      required and, if so, extend the design without breaking `qm-prompts`.
