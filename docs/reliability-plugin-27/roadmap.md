# Delivery roadmap

Parent [#27](https://github.com/dpupek/quick-memory-mcp/issues/27). Complete each numbered phase and record evidence in its issue before advancing. The phase 4 pilot dependency is the tool-measurement checklist in [#25](https://github.com/dpupek/quick-memory-mcp/issues/25). The remaining health diagnostics can continue independently.

## Phase 1 - Enforce project and entry permissions across MCP and HTTP

[#28](https://github.com/dpupek/quick-memory-mcp/issues/28). An administrator can assign a project tier and get the same result through either client.

- [ ] Write the expected Reader, Editor, Curator and Admin matrix for read, create, update, promotion, delete, and permanent-entry operations. Cover the stored state as well as the requested state.
- [ ] Apply one policy to MCP and HTTP, including shared reads and project discovery. Resolve the permitted project before returning entry data. Replace literal credentials in setup examples with environment references and review any issued examples privately.
- [ ] Run transport-level allow/deny tests against disposable fixtures for every role. A rejected request must leave disk and search state unchanged. Link the error contract results to #26.

## Phase 2 - Upgrade the runtime, audited dependencies, and MCP SDK

[#29](https://github.com/dpupek/quick-memory-mcp/issues/29). The Windows service, CLI, and tests run on a supported runtime and preserve existing client contracts.

- [ ] Retarget the worker, MemoryCtl and tests to .NET 10 LTS. Align hosting and logging packages and update installer prerequisites. Recheck package support at implementation time.
- [ ] Resolve transitive advisories for Newtonsoft.Json and System.Text.Json through their owning packages. Exercise SQLite audit logging and Azure Blob backup and restore, and save the resolved dependency audit.
- [ ] Migrate both MCP packages together from 0.4.0-preview.3 in a separate change. Use stable 2.4.0 as the reviewed candidate. Verify discovery, auth, errors (#26), and a real Codex session.
- [ ] On a disposable Windows installation, test install, in-place upgrade, config/data preservation, and rollback. Record build identity and Release test results.

## Phase 3 - Serialize store commits and make failed writes retryable

[#30](https://github.com/dpupek/quick-memory-mcp/issues/30). When two clients save a handoff, each acknowledged write survives restart and is searchable.

- [ ] Serialize mutations, snapshot publication, and watcher reload per store. Mark data clean only after persistence succeeds. Define how external edits and self-generated watcher events are handled.
- [ ] Add a batch commit path for import so one batch produces one durable write and index refresh. Keep the existing JSONL format readable.
- [ ] Use controlled interleavings and injected write failures to verify overlapping saves, cancellation, retry, watcher reload, and restart. Compare on-disk entries, in-memory reads and search results. Confirm unrelated stores can progress independently.

## Phase 4 - Measure MCP tool usage and outcomes

[#25](https://github.com/dpupek/quick-memory-mcp/issues/25). An operator can distinguish searches, project lookups, writes, and failures on the shared MCP route.

- [ ] Instrument the tool dispatch path with tool name, resolved project, outcome, latency and response size. Include tool failures inside successful HTTP responses. Use bounded labels and omit credentials, queries and entry bodies.
- [ ] Compare a known mix of search, discovery, write, and denied requests with the counters. Distinguish transport traffic from tool executions and count each tool call once.
- [ ] Record baseline calls and response sizes for the pilot. Retain the original issue's broader diagnostics work as follow-up within #25.

## Phase 5 - Deliver compact, case-aware retrieval and measure search quality

[#31](https://github.com/dpupek/quick-memory-mcp/issues/31). A returning user can find a handoff or decision without loading twenty full records and their vectors.

- [ ] Add an opt-in summary view for cold start and search with ID, project/shared origin, title, date, kind, case and bounded excerpt. Use getEntry for full details. Preserve current response defaults for existing clients.
- [ ] Resolve an exact project key or unique permitted slug. Handle missing or ambiguous mappings explicitly. Search case identifiers in structured metadata, titles and text, including older records.
- [ ] Apply project, shared and tag filters before the result limit. Reuse the query for tags (#12) and glossary (#13). Confirm a matching record outside the initial unfiltered top results can be found.
- [ ] Freeze a reference set for both pilot cohorts and measure top-five relevance, calls to resume a case and initial reply size. Benchmark the Lucene package family together on a rebuildable index. Record lexical and hash fallback behavior accurately. Defer ONNX inference investment pending measured misses.

## Phase 6 - Pilot the Quick Memory Codex plugin with SysOps and Campus

[#32](https://github.com/dpupek/quick-memory-mcp/issues/32). Both cohorts can resume a case, find a sourced decision and save an explicitly requested handoff through the existing service.

- [ ] Package three skills in the private workspace marketplace. Start with the existing MCP connection, bind the authorized project once per workspace, and document install, update and disable in a fresh Codex session.
- [ ] Run SysOps progress-handoff scenarios and Campus case/decision scenarios. Exercise missing metadata, ambiguous project names, shared provenance, an empty result and a read-only user. Use approved pilot fixtures for writes.
- [ ] Before the trial, select ten known-answer case/decision scenarios per cohort. Record the expected source ID, baseline tool calls, and initial response bytes. Repeat the same cases with the plugin.
- [ ] Proposed pass criteria for each cohort: expected source in the first five for at least 8/10 scenarios, median resume of at most three MCP calls, and median first resume response at least 80% smaller than baseline. Exactly one handoff remains after repeating an explicit save for the same case. Read-only sessions make zero successful writes.
- [ ] Record results per cohort and a decision to expand, revise or stop. Disable the pilot package and retain direct MCP access if a cohort needs further work. Publish aggregate results only.

## Related deliveries

- [ ] [#3](https://github.com/dpupek/quick-memory-mcp/issues/3) follows the batch-commit work in [#30](https://github.com/dpupek/quick-memory-mcp/issues/30). Verify the existing import modes and dry-run. Deliver a scoped export with a round-trip check, then a separately validated atomic replace. A failed replace preserves the old store.
- [ ] [#7](https://github.com/dpupek/quick-memory-mcp/issues/7) can ship independently. Reset and focus the Add Project form after success and retain the user's inputs on failure.

## Decisions and open questions

- Keep JSONL, the existing Windows service and direct MCP access. Revisit storage only if measured reliability or capacity warrants it.
- Start plugin testing with the configured MCP connection. Use the private marketplace for pilot distribution. Keep credentials external and test enabling and disabling in a fresh session.
- The pilot thresholds are proposals. Before the first scored trial, each cohort lead confirms the ten scenarios, expected source IDs, reference build and thresholds. Store fixtures privately. Report top-five hits per ten, median calls per resume, median first-response UTF-8 bytes, and explicit-save outcomes separately for each cohort. Calculate size reduction against the same cohort's baseline median.
- During phase 3, decide how to surface conflicts between simultaneous edits to the same handoff. Validate the selected behavior on disk and after restart.
- After phase 5, decide whether measured retrieval misses justify real ONNX inference. Compare against the lexical baseline before committing to model distribution or vector migration.

[Current behavior and edge cases](edge-cases.md) | [User workflows](workflows.md) | [Responsibilities](crcs.md) | [Stakeholder outcomes](stakeholder-summary.md)
