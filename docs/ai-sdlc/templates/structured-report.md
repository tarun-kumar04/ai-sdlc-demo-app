# Structured Report Template

Use this common envelope for all assignment evidence. Save machine-readable reports as JSON validated by `schemas/structured-report.schema.json`; Markdown may accompany them but does not replace evidence.

```json
{
  "schema_version": 1,
  "report_type": "specification-compliance | test-review | mutation-analysis | code-review | interaction-import | hook-verification | workflow-evidence",
  "created_at": "<actual ISO-8601 UTC timestamp>",
  "summary": "<concise finding>",
  "findings": [
    {
      "id": "F-001",
      "severity": "critical | high | medium | low | info",
      "title": "<finding title>",
      "file": "<workspace-relative path when applicable>",
      "line": null,
      "evidence": "<observed evidence and source reference>",
      "impact": "<why it matters>",
      "recommendation": "<actionable recommendation>",
      "verdict": "open | fixed | dismissed | deferred | not-applicable",
      "verdict_reason": "<required rationale; never leave implicit>"
    }
  ],
  "checks": [
    {
      "command": "<exact command>",
      "result": "passed | failed | not-run",
      "evidence": "<actual output reference>"
    }
  ],
  "sources": [
    {
      "source": "<file/report/session event>",
      "provenance": "<path, ID, or event reference>",
      "record_origin": "historical_import | live_hook | documented_summary"
    }
  ],
  "limitations": []
}
```

## Report-specific content

- **Specification compliance:** put requirement-to-artifact coverage and unresolved assumptions in findings; evidence cites the assignment and exact OpenSpec path/section.
- **Test review:** include behavior covered, missing boundary/negative/concurrency cases, and exact test commands/results.
- **Mutation analysis:** include previous/current score, percentage-point difference, outcome counts, mutant IDs/locations/replacements, test rationale, and limitations.
- **Code review:** every finding needs severity, file/line, evidence, impact, recommendation, and disposition (`fixed`, `dismissed`, or `deferred`) with reason. `open` is acceptable only before final disposition.
- **Interaction import:** include source accessibility, counts imported/skipped/duplicates/rejected, timestamp coverage, redaction counts/limitations, and source references. Summaries must be marked as summaries.
- **Hook verification:** distinguish schema/config checks and fixture invocations from actual Copilot runtime execution; cite event and evidence for live verification.
- **Workflow evidence:** list agent invocations, real sub-agent identity/result, skills exercised, tests, validation, commits (if any), and limitations.
