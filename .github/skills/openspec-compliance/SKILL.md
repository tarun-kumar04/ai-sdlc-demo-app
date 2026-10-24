---
name: openspec-compliance
description: Compare assignment requirements with OpenSpec proposal, design, specs, and tasks; identify compliance gaps, conflicts, and unconfirmed assumptions. Use before planning review or implementation when assignment requirements exist.
---

# OpenSpec Compliance Review

## Inputs

- Assignment text or path, with provenance.
- Active change name, if known.
- Repository `AGENTS.md` and relevant implementation context.

## Procedure

1. Read `AGENTS.md`, assignment source, and `openspec status --change <name> --json`.
2. Read the proposal, all delta specs, design, and tasks identified by status. Do not infer filenames when status supplies paths.
3. Build a requirement matrix: assignment requirement, artifact coverage, evidence (path/line/scenario), outcome (`covered`, `partial`, `missing`, `conflict`), and note.
4. Separate exact assignment language from implementation assumptions and unresolved questions. Do not upgrade a proposed design into a requirement.
5. Check task staging and commit separation, including whether acceptance criteria and tests are represented.
6. Do not edit production code. If asked to revise artifacts, follow the active OpenSpec update workflow and confirmation requirements.
7. Where appropriate run `openspec validate <change>`; report actual output.

## Output

Use `docs/ai-sdlc/schemas/structured-report.schema.json` with report type `specification-compliance`. Include summary, requirement matrix/findings, unresolved assumptions, recommendations, files inspected, and commands actually run. Use `documented_summary` provenance only for summarized historical conversation, not as assignment evidence.
