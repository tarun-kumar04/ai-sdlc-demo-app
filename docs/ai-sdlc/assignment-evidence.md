# AI-in-SDLC Assignment Evidence

Status: evidence index for local review; entries are limited to checks actually performed in this workspace.

## Custom agents

Five workspace profiles are under `.github/agents/`: Specification Analyst, Test Engineer, Code Reviewer, Mutation Testing Specialist, and Workflow Orchestrator. Their frontmatter follows the VS Code `.agent.md` format. Read-only agents receive read/search tools; test/mutation/orchestration agents receive execution/delegation capabilities where available.

## Skills

Existing OpenSpec skills remain in `.github/skills/openspec-*`. Added reusable skills:

- `openspec-compliance`
- `approval-rule-testing`
- `stryker-mutation-testing`
- `structured-code-review`
- `ai-interaction-audit`

Skills are discovered through the supported `.github/skills/<name>/SKILL.md` layout. Availability does not prove automatic invocation.

## Hook implementation and verification

- Configuration: `.github/hooks/ai-interaction-audit.json` (version 1, PascalCase VS Code-compatible Copilot events).
- Logger: `scripts/log_ai_interaction.py`, with shared normalization in `scripts/ai_interactions/log.py`.
- Configured lifecycle events: `SessionStart`, `UserPromptSubmit`, `PreToolUse`, `PostToolUse`, `PostToolUseFailure`, `SubagentStart`, `SubagentStop`, `Stop`, and `SessionEnd`.
- Fixture tests verify logger behavior. During this implementation session, the canonical log received live prompt, tool call/result, subagent start/result, and stop events. This is observed evidence for those event categories only; a new-session check after installing the config has not been performed, and startup/end/failure events are not claimed live-verified.
- No lifecycle hook can force model response formatting; agents/skills and `scripts/validate_structured_report.py` define and validate report structure instead.

## Canonical interaction log

- Path: `logs/ai-interactions.jsonl`.
- One JSON object per line, validated against `docs/ai-sdlc/schemas/ai-interaction-record.schema.json`. Required fields: `event_id`, `timestamp` (nullable), `timestamp_status`, `source`, `event_type`, `record_origin`, `content`, `source_reference`, `redaction_applied`, and `is_summary`; `session_id` is optional. `content_truncated` is included when applicable.
- Historical source scope, import result, and limitations: `docs/ai-sdlc/historical-sources.md`.
- Repeat import is deduplicated by stable event IDs. Hook retries dedupe when their normalized event fingerprint is stable. Redaction is best effort, not a guarantee.
- Historical import scope defaults to explicit paths and immediate directory contents; nested traversal requires `--recursive`. The canonical destination log is excluded as an import source.

## Structured reports

- Contract: `docs/ai-sdlc/schemas/structured-report.schema.json`.
- Human template: `docs/ai-sdlc/templates/structured-report.md`.
- Validator: `python3 scripts/validate_structured_report.py <report.json>`.
- Review findings must have an explicit verdict and rationale.

## Delegation evidence

The implementation session directly invoked the named Workflow Orchestrator, Specification Analyst, Test Engineer, Mutation Testing Specialist, and Code Reviewer profiles. Specialist findings were reconciled into the implementation; they were read-only and did not change application approval behavior. The Workflow Orchestrator reported that its context did not expose its expected native `task` delegation surface, so its own nested-delegation claim is not relied upon. The direct named-agent invocations are the delegation evidence.

## Verification record

The verification script/report will record exact test, OpenSpec, hook-fixture, and validation commands and observed outcomes. Do not infer successful live hook execution from fixture tests or configuration presence. See the generated evidence report in this folder after verification.
