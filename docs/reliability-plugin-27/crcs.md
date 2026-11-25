# Responsibilities and collaborators

These are implementation owners to inspect. Suggested changes reuse the worker's current namespaces and services. The plugin lives in the client package and calls the existing server contract.

| Owner | Responsibility in the plan | Collaborators | Workflows |
| --- | --- | --- | --- |
| `QuickMemoryServer.Worker.Services.ApiKeyAuthorizer` and `McpAuthorizationContext` | Resolve effective project access and provide one policy decision to the two transports. | HTTP route handlers in Program.cs, MemoryMcpTools, configured permission tiers | W1, W5 |
| `QuickMemoryServer.Worker.Services.MemoryRouter` and `MemoryMcpTools` | Resolve the permitted endpoint, compose compact results and retain the full-record contract. | SearchEngine, MemoryStore, SchemaService, HTTP handlers | W1, W3, W4, W5 |
| `QuickMemoryServer.Worker.Memory.MemoryStore` and `QuickMemoryServer.Worker.Persistence.JsonlRepository` | Order commits per store, publish durable snapshots, and recover from failures. | DebouncedFileWatcher, SearchEngine, GraphIndex, Admin import | W2, W5 |
| `QuickMemoryServer.Worker.Search.SearchEngine` and `LuceneStore` | Filter eligible entries before limiting ranked results. Rebuild indexes from existing JSONL. | MemoryMcpTools, graph/vector indexes, embedding generator | W3, W4 |
| `QuickMemoryServer.Worker.Diagnostics.ObservabilityMetrics` and `HealthMetricsStore` | Record tool-level outcomes and latency and report pilot measures with bounded labels. | MCP tool filter, HTTP middleware and Admin Health view | W2, W6 |
| Windows installer, MemoryCtl and backup services | Preserve config and store compatibility across upgrade, restore and rollback. | Worker publish output, SQLite audit sink, Azure Blob uploader | W2 |
| Quick Memory Codex plugin | Package workspace project selection and the resume, decision and explicit-save workflows. | Existing MCP connection, authorized user, private marketplace, pilot leads | W3, W4, W5, W6 |
