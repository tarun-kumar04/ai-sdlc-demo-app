---
name: ai-interaction-audit
description: Audit canonical AI interaction logs for completeness, provenance, event types, timestamps, redaction, import idempotency, and historical-source coverage. Use when importing interactions, reviewing hooks, or preparing assignment evidence.
---

# AI Interaction Audit

## Inputs

- Canonical log path (default `logs/ai-interactions.jsonl`).
- Sources explicitly in scope (repository-specific exports/session IDs only).
- `.github/hooks/ai-interaction-audit.json`, logger/importer, schema, and evidence docs.

## Procedure

1. Inspect sources before importing. Restrict session stores to the relevant repository and explicit session ID; never scan unrelated private history.
2. Distinguish raw events from `documented_summary`; never call a summary a verbatim prompt/response. Retain source reference and original timestamp if available; otherwise use null and `timestamp_status: unknown`.
3. Verify required schema fields, valid event types/origins, stable IDs, one-object-per-line JSONL, and consistent schema between live and historical records.
4. Check that secrets are redacted before append, but state that pattern-based redaction is best effort. Do not print or copy unredacted credentials into reports.
5. Verify append behavior and rerun import to confirm duplicates are skipped. Record imported/skipped/duplicate/rejected counts and malformed/unsupported sources; never silently omit them.
6. For hooks, inspect the actual hook reference and payload format. Fixture tests prove normalization only. A live hook claim requires a new Copilot session and captured evidence from that runtime.
7. Historical import command: `python3 scripts/import_ai_interactions.py <explicit-source>`. Scoped Copilot SQLite import requires `--sqlite-copilot-session <db> --session-id <id> --expected-repository <exact-url>`. Use `--dry-run` before writing; `--strict` returns failure for rejected source records.
8. Run `python3 -m unittest discover -s tests/ai_sdlc -v`, validate reports with `python3 scripts/validate_structured_report.py <report.json>`, and run `git diff --check`.

## Output

Return an audit report with source accessibility, counts, provenance, timestamp coverage, redaction caveats, idempotency result, hooks configured vs live-verified, exact commands/results, and remaining limitations. Use the shared report schema. Do not claim complete historical coverage or live hook execution without evidence.
