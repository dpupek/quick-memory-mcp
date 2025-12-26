# Quick Memory Server End-User Help

This guide is for **people chatting with an agent** (Codex/ChatGPT/etc.). It focuses on how to get the most value from Quick Memory and when to ask the agent to store or retrieve context. It does **not** cover API keys, server endpoints, or installation.

## How to use Quick Memory with your agent

Use these patterns to make memory a habit in conversations:

1. **Start every new session with a recap**
   Ask the agent to call `listRecentEntries` for the relevant project and summarize what changed recently.

2. **Search before doing new work**
   Ask the agent to run `searchEntries` with your topic so it reuses prior decisions and avoids duplicate work.

3. **Store the result when you finish**
   When you make a decision, fix a tricky bug, or learn a new operational detail, ask the agent to save it with `upsertEntry`.

4. **Update existing entries when things change**
   If a decision evolves, ask the agent to use `patchEntry` rather than creating a conflicting new note.

5. **Link related work**
   When a new entry depends on an earlier one, ask the agent to add a relation so future agents can follow the chain.

## Suggested prompts (copy/paste)

### Cold start (new session)
> “Before we begin, call `listRecentEntries` and summarize the last 10–20 updates for this project. Highlight any open questions or follow‑ups.”

### Research or investigation
> “Search Quick Memory for ‘<topic>’ and summarize anything relevant before proposing changes.”

### Record a new lesson
> “We just finished <work>. Please save a new entry using `upsertEntry` with a short title, 3–6 tags, and a body that captures the decision, why we chose it, and validation steps.”

### Update an existing decision
> “Update the existing entry about ‘<decision>’ to reflect the new approach, and use `patchEntry` so we don’t create duplicates.”

### Create a cross‑reference
> “Add a relation from the new entry to the earlier one about ‘<topic>’ so future readers can follow the trail.”

## What to store vs. what to skip

Store:
- Decisions (what we chose and why)
- Repeatable procedures (build/test/deploy steps)
- Important quirks (edge cases, surprising behavior, timeouts)
- Validation steps and results

Skip:
- Large raw logs or transcripts (summarize instead)
- One‑off chats with no reusable insight

## Troubleshooting (for end users)

- If the agent says it cannot access memory, ask it to run `listProjects` and then retry `listRecentEntries`.
- If the agent reports “endpoint not found,” ask it to re‑check `listProjects` and pick the correct project key.
- If responses seem stale, ask the agent to run `searchEntries` with a fresh query and then `listRecentEntries`.
