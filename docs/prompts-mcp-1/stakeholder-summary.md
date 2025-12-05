# Stakeholder Summary – MCP Prompts Backed by Quick Memory Entries

## Admins / Operators

- Gain a central place (`qm-prompts`) to manage high-value prompt
  templates without touching code.
- Can version, back up, and restore prompts using existing Quick Memory
  mechanisms.
- Control who can edit prompts via endpoint permissions and tiers.

## Developers / Support Engineers

- Get consistent, discoverable prompt recipes through MCP instead of
  ad-hoc copy/paste from docs.
- Can quickly prime agents for:
  - First-time onboarding on a project.
  - Cold starts after idle periods.
  - Structured recording of new lessons and investigations.

## AI Agents

- Have a structured way to:
  - Discover available prompts (`prompts/list`).
  - Request templates with arguments (`prompts/get`).
  - Use them to bootstrap conversations and guide how to store entries
    via `upsertEntry`.

## Organization / Future Teams

- Prompt logic lives alongside other operational knowledge in Quick
  Memory, so new teams can inherit both:
  - Historical context (entries), and
  - The recommended ways to work with that context (prompts).

