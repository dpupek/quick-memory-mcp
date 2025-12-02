# Load Testing Guide

This folder contains k6 scenarios you can run locally (or in CI) to validate the Quick Memory Server under traffic.

## Prerequisites
- Install [k6](https://k6.io/docs/getting-started/installation/).
- Ensure the server is running with Prometheus metrics enabled (`/metrics`) and that your API key has sufficient permissions.

## Environment Variables
- `QMS_BASE_URL` – Service base URL (default: `http://localhost:5080`).
- `QMS_API_KEY` – API key to authenticate MCP requests (required).
- `QMS_PROJECT` – Project/endpoint to target (default: `projectA`).

## Scenarios
1. **Read-heavy search burst**
   ```bash
   k6 run -e QMS_API_KEY=your_key k6/search-read-heavy.js
   ```
   Stages RPS from 5 → 60 to catch latency spikes.

2. **Mixed read/write workload**
   ```bash
   k6 run -e QMS_API_KEY=your_key k6/mixed-read-write.js
   ```
   Exercises `searchEntries`, `upsertEntry`, and `patchEntry` under steady load.

## Observability Checklist
During tests, capture:
- `/metrics` snapshot before/after to record request counts and latency buckets.
- Structured logs in `logs/quick-memory-server-*.log` for per-request durations.
- System resource usage (`dotnet-counters monitor --process <pid>`), especially for long runs.

## Reporting
For each run, record:
- Scenario name, duration, and target RPS.
- k6 summary (RPS, latency p(95), error rate).
- Observed Prometheus metrics (qms_mcp_requests_total, qms_mcp_request_duration_seconds) and any alerts.
- Notes on bottlenecks or tuning adjustments.

Store reports under `load-tests/reports/YYYYMMDD.md` to track performance over time.
