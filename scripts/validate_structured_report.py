#!/usr/bin/env python3
"""Validate structured reports or canonical interaction records using repository schemas."""
from __future__ import annotations

import argparse
import json
import re
import sys
from datetime import datetime
from pathlib import Path
from typing import Any

REPORT_TYPES = {
    "specification-compliance", "test-review", "mutation-analysis", "code-review",
    "interaction-import", "hook-verification", "workflow-evidence"
}
SEVERITIES = {"critical", "high", "medium", "low", "info"}
VERDICTS = {"fixed", "dismissed", "deferred", "open", "not-applicable"}
EVENT_TYPES = {
    "session_start", "session_end", "prompt", "response", "tool_call", "tool_result",
    "tool_failure", "agent_start", "agent_result", "agent_stop", "error",
    "context_compaction", "documented_summary"
}
ORIGINS = {"historical_import", "live_hook", "documented_summary"}
SHA256_EVENT_ID = re.compile(r"^sha256:[a-f0-9]{64}$")


def _string(value: Any, *, nonempty: bool = False, max_length: int | None = None) -> bool:
    return isinstance(value, str) and (not nonempty or bool(value.strip())) and (max_length is None or len(value) <= max_length)


def _datetime(value: Any) -> bool:
    if not isinstance(value, str):
        return False
    try:
        return datetime.fromisoformat(value.replace("Z", "+00:00")).tzinfo is not None
    except ValueError:
        return False


def validate_report(value: Any) -> list[str]:
    errors: list[str] = []
    if not isinstance(value, dict):
        return ["report root must be a JSON object"]
    allowed = {"schema_version", "report_type", "created_at", "summary", "findings", "checks", "sources", "limitations"}
    for field in value.keys() - allowed:
        errors.append(f"unexpected top-level field: {field}")
    required = ("schema_version", "report_type", "created_at", "summary", "findings", "checks", "sources")
    for field in required:
        if field not in value:
            errors.append(f"missing required top-level field: {field}")
    version = value.get("schema_version")
    if not isinstance(version, int) or isinstance(version, bool) or version != 1:
        errors.append("schema_version must equal integer 1")
    if not isinstance(value.get("report_type"), str) or value.get("report_type") not in REPORT_TYPES:
        errors.append("report_type is not supported")
    if not _datetime(value.get("created_at")):
        errors.append("created_at must be a timezone-qualified ISO-8601 date-time")
    if not isinstance(value.get("summary"), str):
        errors.append("summary must be a string")
    for field in ("findings", "checks", "sources"):
        if not isinstance(value.get(field), list):
            errors.append(f"{field} must be an array")
    if "limitations" in value and (not isinstance(value["limitations"], list) or not all(isinstance(item, str) for item in value["limitations"])):
        errors.append("limitations must be an array of strings")

    findings = value.get("findings", [])
    if isinstance(findings, list):
        required_finding = ("id", "severity", "title", "evidence", "impact", "recommendation", "verdict", "verdict_reason")
        for index, finding in enumerate(findings):
            prefix = f"findings[{index}]"
            if not isinstance(finding, dict):
                errors.append(f"{prefix} must be an object")
                continue
            missing = [field for field in required_finding if field not in finding]
            if missing:
                errors.append(f"{prefix} missing fields: {', '.join(missing)}")
            for field in ("id", "title", "evidence", "impact", "recommendation", "verdict_reason"):
                if field in finding and not _string(finding[field], nonempty=True):
                    errors.append(f"{prefix}.{field} must be a non-empty string")
            if "severity" in finding and (not isinstance(finding["severity"], str) or finding["severity"] not in SEVERITIES):
                errors.append(f"{prefix}.severity is not supported")
            if "verdict" in finding and (not isinstance(finding["verdict"], str) or finding["verdict"] not in VERDICTS):
                errors.append(f"{prefix}.verdict is not supported")
            if "file" in finding and not isinstance(finding["file"], str):
                errors.append(f"{prefix}.file must be a string")
            line = finding.get("line")
            if line is not None and (not isinstance(line, int) or isinstance(line, bool) or line < 1):
                errors.append(f"{prefix}.line must be null or a positive integer")
    for field in ("checks", "sources"):
        items = value.get(field, [])
        if isinstance(items, list) and not all(isinstance(item, dict) for item in items):
            errors.append(f"{field} entries must be objects")
    return errors


def validate_interaction(value: Any) -> list[str]:
    errors: list[str] = []
    if not isinstance(value, dict):
        return ["interaction record must be a JSON object"]
    allowed = {
        "event_id", "timestamp", "timestamp_status", "source", "session_id", "event_type",
        "record_origin", "content", "source_reference", "redaction_applied", "is_summary",
        "content_truncated",
    }
    for field in value.keys() - allowed:
        errors.append(f"unexpected interaction field: {field}")
    required = ("event_id", "timestamp", "timestamp_status", "source", "event_type", "record_origin", "content", "source_reference", "redaction_applied", "is_summary")
    for field in required:
        if field not in value:
            errors.append(f"missing required interaction field: {field}")
    event_id = value.get("event_id")
    if not isinstance(event_id, str) or not SHA256_EVENT_ID.fullmatch(event_id):
        errors.append("event_id must be sha256:<64 lowercase hexadecimal characters>")
    timestamp_status = value.get("timestamp_status")
    if not isinstance(timestamp_status, str) or timestamp_status not in ("known", "unknown"):
        errors.append("timestamp_status must be known or unknown")
    elif timestamp_status == "known" and not _datetime(value.get("timestamp")):
        errors.append("known timestamps require a timezone-qualified ISO-8601 timestamp")
    elif timestamp_status == "unknown" and value.get("timestamp") is not None:
        errors.append("unknown timestamps must use null")
    if not _string(value.get("source"), nonempty=True, max_length=1024):
        errors.append("source must be a non-empty string of at most 1024 characters")
    if "session_id" in value and not _string(value["session_id"], max_length=1024):
        errors.append("session_id must be a string of at most 1024 characters")
    if not isinstance(value.get("event_type"), str) or value.get("event_type") not in EVENT_TYPES:
        errors.append("event_type is not supported")
    if not isinstance(value.get("record_origin"), str) or value.get("record_origin") not in ORIGINS:
        errors.append("record_origin is not supported")
    if value.get("record_origin") == "documented_summary" and value.get("is_summary") is not True:
        errors.append("documented_summary records must set is_summary to true")
    if not _string(value.get("content"), max_length=12032):
        errors.append("content must be a string of at most 12032 characters")
    if not _string(value.get("source_reference"), nonempty=True, max_length=1024):
        errors.append("source_reference must be a non-empty string of at most 1024 characters")
    for field in ("redaction_applied", "is_summary"):
        if not isinstance(value.get(field), bool):
            errors.append(f"{field} must be a boolean")
    if "content_truncated" in value and not isinstance(value["content_truncated"], bool):
        errors.append("content_truncated must be a boolean")
    return errors


def validate(value: Any, kind: str = "auto") -> list[str]:
    if kind == "auto":
        kind = "interaction" if isinstance(value, dict) and "event_id" in value else "report"
    if kind == "interaction":
        return validate_interaction(value)
    if kind == "report":
        return validate_report(value)
    return [f"unsupported validation kind: {kind}"]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("input", type=Path, nargs="+", help="JSON report or JSONL interaction records")
    parser.add_argument("--kind", choices=("auto", "report", "interaction"), default="auto")
    args = parser.parse_args()
    failures = 0
    for path in args.input:
        try:
            if path.suffix.lower() == ".jsonl" and args.kind in ("auto", "interaction"):
                with path.open(encoding="utf-8") as stream:
                    for line_number, line in enumerate(stream, 1):
                        if not line.strip():
                            continue
                        try:
                            value = json.loads(line)
                        except json.JSONDecodeError as error:
                            print(f"{path}:{line_number}: invalid JSON: {error.msg}", file=sys.stderr)
                            failures += 1
                            continue
                        for error in validate(value, "interaction"):
                            print(f"{path}:{line_number}: {error}", file=sys.stderr)
                            failures += 1
            else:
                value = json.loads(path.read_text(encoding="utf-8"))
                kind = args.kind if args.kind != "auto" else ("interaction" if isinstance(value, dict) and "event_id" in value else "report")
                for error in validate(value, kind):
                    print(f"{path}: {error}", file=sys.stderr)
                    failures += 1
        except (OSError, json.JSONDecodeError) as error:
            print(f"{path}: unable to read valid JSON: {error}", file=sys.stderr)
            failures += 1
    if failures:
        return 1
    print(f"validated {len(args.input)} input file(s)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
