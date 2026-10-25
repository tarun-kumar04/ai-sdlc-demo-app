---
name: structured-code-review
description: Review a user-requested or staged diff for correctness, security, regressions, maintainability, and test quality with evidence and mandatory finding dispositions. Use for code review and final change review.
---

# Structured Code Review

## Inputs

- Pull request URL or `owner/repository` plus pull request number. Prefer GitHub MCP when the PR is hosted on GitHub.
- Diff range or changed files when reviewing a local-only change.
- `AGENTS.md`, active OpenSpec artifacts, and applicable tests.
- Review scope (read-only unless user explicitly authorizes edits).

## Procedure

1. **Establish remote PR scope first.** For a GitHub PR, use GitHub MCP `pull_request_read` with `method: get` to capture PR number, URL, state, base/head branch and SHA, changed-file count, and commit count. Fetch the complete PR patch with `method: get_diff`; fetch all changed-file pages with `method: get_files` and all commit pages with `method: get_commits` as needed. Record every page/result needed to establish completeness. Do not infer the PR head from the local checkout.
   - If a custom agent is used, confirm the host has enabled the necessary GitHub MCP read tools for that agent: PR read (`get`, `get_diff`, `get_files`, `get_commits`, `get_status`, `get_check_runs`, `get_review_comments`, `get_reviews`, `get_comments`), repository file/commit reads, and pagination. Tool names in `.agent.md` must match the registered MCP tools. Do not use write-capable GitHub tools for a code review.
2. **Compare local state only as corroboration.** If local Git access is available, inspect `git status`, `git log <base>..<head>`, `git diff --stat <base>...<head>`, and `git diff --name-status <base>...<head>`. Confirm the local `HEAD` SHA equals the PR head SHA before describing the local patch as the PR patch. If they differ, review the remote PR diff and state that local test/build results apply only to the local tree. Preserve pre-existing user changes.
3. **Inspect the complete intended patch.** Review the entire fetched PR diff, not just selected files or a workspace snapshot. If the response is truncated or too large, retrieve and inspect it file-by-file using the PR file list and repository contents at the PR head; explicitly list any inaccessible files. Inspect relevant surrounding code and tests.
4. Read `AGENTS.md`, the active OpenSpec proposal, design, tasks, specification and config, plus relevant agent/skill/hook definitions and evidence. Compare implementation and commit history against these constraints and requirements.
5. Evaluate correctness, authorization/session trust, input validation, state integrity/concurrency, regressions, maintainability, test quality, unrelated changes, generated artifacts, and sensitive-data exposure. Check existing tests before reporting gaps. Treat unresolved business rules as policy ambiguity, not invented requirements.
6. Do not edit the code or documentation being reviewed. If a fix is separately authorized, record review and remediation separately.
7. Give each actionable finding a stable ID and severity (`critical`, `high`, `medium`, or `low`), title, workspace-relative file/line or commit, evidence from the actual diff/spec, impact, recommendation, and classification (confirmed defect, potential risk, or policy ambiguity).
8. Assign a disposition for every finding: `fixed` only when the fix is present and verified; `dismissed` with evidence and reason; `deferred` with reason and owner/condition; or `open` when PR scope, evidence, or policy is unresolved (report it as pending in prose). Do not call unreviewed findings fixed.
9. Report positive coverage and remaining blind spots separately. Run tests, builds, validation, or hook checks only when useful and available; report exact commands and observed outcomes. Do not claim GitHub, Git, tests, or hooks were inspected/run unless they were.
10. Use `docs/ai-sdlc/schemas/structured-report.schema.json` as the common report contract and validate when the repository validator is available. The reviewer must have workspace edit permission to save the completed report under `docs/ai-sdlc/evidence/` (or the user-specified path). Create or update only that report; do not modify reviewed source or unrelated files. If write permission is unavailable, return the review and state that saving was blocked.

## Output

Begin with review scope: base commit, PR head SHA, local-HEAD match status, files/areas inspected, and inaccessible resources. Return actionable findings, positive coverage, blind spots, checks actually performed, and a disposition table for every finding. Do not approve the PR unless the inspected evidence supports that conclusion. If remote PR access fails, say so and label any local-only review as partial; do not present it as a full PR review.
