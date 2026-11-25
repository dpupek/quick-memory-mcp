# Baseline and edge cases

## Reviewed on November 25, 2026

The repository review used `8ab5063` and passed all 49 Release tests. The sampled service reported `1.0.0+local`, which does not identify its source commit. The [spec](../spec.md) describes intended behavior. The observations below describe the reviewed code or a dated usage sample.

| Observation | Implication for design and validation |
| --- | --- |
| Two pilot stores accounted for 857 of 1,247 entries across 19 stores. The latest 20 SysOps items were progress updates. Campus mixed case notes, decisions, findings and progress. | Exercise handoff and decision workflows separately. Keep the case text and labels used for scoring in the private pilot workspace. |
| Eight sampled local Codex session logs contained 212 search invocations, 66 project listings and one upsert. These counts reflect identifiable invocations on one workstation. Direct MCP traffic was grouped under a generic HTTP route label. | Bind the project once and establish a measured server baseline. Track successful results and tool errors independently of HTTP status. |
| Full cold starts returned 303,527 characters for SysOps and 153,278 for Campus. Recent results included embeddings. A documented project slug failed when used as an endpoint key. | Preserve the full response for existing callers. Measure UTF-8 bytes for the new pilot baseline, omit vectors from summaries, bound excerpts and handle unique or ambiguous slugs explicitly. |
| The [tool handlers](../../src/QuickMemoryServer.Worker/Services/MemoryMcpTools.cs) and [HTTP routes](../../src/QuickMemoryServer.Worker/Program.cs) implement role-sensitive operations separately. | Test both entry points against the same policy, including the existing and requested tier/permanence of an entry. Include endpoint overrides and shared reads. |
| [MemoryStore](../../src/QuickMemoryServer.Worker/Memory/MemoryStore.cs) clears its dirty flag before persistence finishes. [JsonlRepository](../../src/QuickMemoryServer.Worker/Persistence/JsonlRepository.cs) uses a per-path temporary file. Saves rebuild indexes and watcher notifications can initiate reloads. | Exercise simultaneous saves, cancellation, external edits, self-generated notifications and restart. Define the outcome of overlapping edits to the same record. |
| The worker and tests target .NET 9 and MemoryCtl targets .NET 8. The dependency audit flagged Newtonsoft.Json and System.Text.Json transitively. Both MCP references used a preview SDK. | Capture the resolved graph on upgrade. Qualify the Windows service, CLI, SQLite audit logging, backup/restore and Streamable HTTP with the versions to ship. |
| [SearchEngine](../../src/QuickMemoryServer.Worker/Search/SearchEngine.cs) uses vectors when provided. The [ONNX generator](../../src/QuickMemoryServer.Worker/Embeddings/OnnxEmbeddingGenerator.cs) returns hash vectors. Tags are filtered after the top results are selected. | Include exact case IDs, paraphrases, old entries without structured case fields, and a tagged match below the unfiltered cutoff. Record real ranking misses before choosing an embedding model. |

## Failure cases that must feed back into workflows

- **W1/W5:** A denied edit, promotion or permanent-entry change must preserve the previous entry on disk and in search. Verify with disposable data.
- **W2/W5:** If a write or index refresh fails, the user needs an accurate outcome. A retry or restart must reconcile the durable record and its searchable view. A failed import replacement must leave the original store readable.
- **W3/W4:** Duplicate slugs, missing project mappings, absent case metadata, and a shared item with a similar title must never cause an implicit cross-project save. Expose the selected origin on each result.
- **W6:** A cohort with strong aggregate scores can still have consistently bad decisions or handoffs. Keep scenario-level failure reasons privately and publish per-cohort totals.
