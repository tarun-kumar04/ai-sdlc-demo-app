---
name: structured-code-review
description: Review a user-requested or staged diff for correctness, security, regressions, maintainability, and test quality with evidence and mandatory finding dispositions. Use for code review and final change review.
---

# Structured Code Review

## Inputs

- Diff range or changed files.
- `AGENTS.md`, active OpenSpec artifacts, and applicable tests.
- Review scope (read-only unless user explicitly authorizes edits).

## Procedure

1. Inspect `git status`, the complete intended diff, relevant surrounding source, and tests. Preserve pre-existing user changes.
2. Evaluate correctness, authorization/session trust, input validation, state integrity/concurrency, regressions, maintainability, and test coverage.
3. Do not edit the code being reviewed. If a fix is separately authorized, record review and remediation separately.
4. Give each finding a stable ID and severity (`critical`, `high`, `medium`, `low`, `info`), title, workspace-relative file and line, evidence, impact, and recommendation.
5. Every finding must have a disposition: `fixed`, `dismissed`, or `deferred`. Include reason/evidence for dismissed findings and reason plus owner/condition for deferred findings. Do not call unreviewed findings fixed.
6. Use `docs/ai-sdlc/schemas/structured-report.schema.json` as the common report contract. Validate if the repository schema validator is available.
7. Run or cite tests only when actually executed; report exact commands and outcomes.

## Output

Return a structured code-review report with summary/verdict, findings, dispositions, inspected files, checks, and remaining risks. If there are no findings, state the scope and evidence reviewed.
