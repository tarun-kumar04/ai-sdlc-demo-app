#!/usr/bin/env python3
"""Idempotently import explicit JSON/JSONL exports or one selected Copilot SQLite session."""
from __future__ import annotations

import argparse
import hashlib
import json
import sqlite3
import sys
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
SUPPORTED_EVENT_TYPES = {
    "session_start", "session_end", "prompt", "response", "tool_call", "tool_result",
    "tool_failure", "agent_start", "agent_result", "agent_stop", "error",
    "context_compaction", "documented_summary",
}
sys.path.insert(0, str(Path(__file__).resolve().parent))
from ai_interactions.log import append_records, make_record  # noqa: E402


def _type_for(record: dict[str, Any]) -> str | None:
    value = record.get("event_type") or record.get("type")
    if isinstance(value, str) and value:
        return value.lower()
    role = record.get("role") or record.get("speaker")
    if isinstance(role, str):
        return {"user": "prompt", "assistant": "response", "tool": "tool_result"}.get(role.lower())
    return None


def _content_for(record: dict[str, Any]) -> Any:
    for key in ("content", "message", "text", "prompt", "response", "tool_input", "tool_result"):
        if key in record:
            return record[key]
    return None


def _normalize_export_record(record: Any, source_ref: str, default_source: str) -> tuple[dict[str, Any] | None, str | None]:
    if not isinstance(record, dict):
        return None, "record is not a JSON object"
    event_type = _type_for(record)
    content = _content_for(record)
    if event_type is None or content is None:
        return None, "record lacks a recognizable event type/role or content"
    if event_type not in SUPPORTED_EVENT_TYPES:
        return None, f"unsupported event type: {event_type}"
    source = record.get("source") if isinstance(record.get("source"), str) else default_source
    session_id = record.get("session_id") or record.get("sessionId")
    summary_value = record.get("is_summary", record.get("isSummary", False))
    if not isinstance(summary_value, bool):
        return None, "is_summary must be a JSON boolean"
    summary = summary_value
    origin = "documented_summary" if summary else "historical_import"
    reference = str(record.get("source_reference") or record.get("id") or source_ref)
    return make_record(
        source=source,
        session_id=session_id if isinstance(session_id, str) else None,
        event_type=event_type,
        origin=origin,
        reference=reference,
        timestamp=record.get("timestamp"),
        content=content,
        is_summary=summary,
    ), None


def _items_from_json(value: Any) -> list[Any]:
    if isinstance(value, list):
        return value
    if isinstance(value, dict):
        # ChatGPT data exports use a node map with linked message objects.
        if isinstance(value.get("mapping"), dict):
            messages = []
            for node_id, node in value["mapping"].items():
                message = node.get("message") if isinstance(node, dict) else None
                if not isinstance(message, dict):
                    continue
                author = message.get("author") or {}
                role = author.get("role") if isinstance(author, dict) else None
                body = message.get("content") or {}
                parts = body.get("parts") if isinstance(body, dict) else None
                content = "\n".join(part for part in parts if isinstance(part, str)) if isinstance(parts, list) else None
                if role in ("user", "assistant") and content:
                    messages.append({
                        "id": message.get("id") or node_id,
                        "role": role,
                        "content": content,
                        "timestamp": message.get("create_time"),
                        "source": "ChatGPT data export",
                    })
            return messages
        for key in ("events", "messages", "turns", "records"):
            if isinstance(value.get(key), list):
                return value[key]
        return [value]
    return [value]


def import_file(path: Path, default_source: str) -> tuple[list[dict[str, Any]], dict[str, Any]]:
    records: list[dict[str, Any]] = []
    report: dict[str, Any] = {"file": str(path), "read": 0, "accepted": 0, "rejected": []}
    try:
        if path.suffix.lower() == ".jsonl":
            items = []
            for line_number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
                if not line.strip():
                    continue
                report["read"] += 1
                try:
                    items.append((line_number, json.loads(line)))
                except json.JSONDecodeError as error:
                    report["rejected"].append({"record": line_number, "reason": f"malformed JSON: {error.msg}"})
        elif path.suffix.lower() == ".json":
            raw = json.loads(path.read_text(encoding="utf-8"))
            items = list(enumerate(_items_from_json(raw), 1))
            report["read"] = len(items)
        else:
            return [], {**report, "rejected": [{"record": None, "reason": "unsupported file extension; expected .json or .jsonl"}]}
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        return [], {**report, "rejected": [{"record": None, "reason": f"cannot read source safely: {type(error).__name__}: {error}"}]}

    for index, item in items:
        path_fingerprint = hashlib.sha256(str(path.resolve()).encode("utf-8")).hexdigest()[:16]
        normalized, reason = _normalize_export_record(item, f"{path.name}#{path_fingerprint}#record={index}", default_source)
        if normalized is None:
            report["rejected"].append({"record": index, "reason": reason})
        else:
            records.append(normalized)
    report["accepted"] = len(records)
    return records, report


def import_copilot_sqlite(path: Path, session_id: str, expected_repository: str) -> tuple[list[dict[str, Any]], dict[str, Any]]:
    report: dict[str, Any] = {"file": path.name, "session_id": session_id, "read": 0, "accepted": 0, "rejected": []}
    try:
        uri = f"file:{path.resolve().as_posix()}?mode=ro"
        with sqlite3.connect(uri, uri=True) as database:
            session = database.execute(
                "SELECT repository FROM sessions WHERE id = ?", (session_id,)
            ).fetchone()
            if session is None:
                return [], {**report, "rejected": [{"record": None, "reason": "session id was not found"}]}
            if session[0] != expected_repository:
                return [], {**report, "rejected": [{"record": None, "reason": "session repository does not match the explicitly requested repository"}]}
            turns = database.execute(
                "SELECT turn_index, timestamp, user_message, assistant_response FROM turns WHERE session_id = ? ORDER BY turn_index",
                (session_id,),
            ).fetchall()
    except (OSError, sqlite3.Error) as error:
        return [], {**report, "rejected": [{"record": None, "reason": f"SQLite source error: {type(error).__name__}: {error}"}]}

    records: list[dict[str, Any]] = []
    for turn_index, timestamp, user_message, assistant_response in turns:
        for role, event_type, content in (
            ("user", "prompt", user_message),
            ("assistant", "response", assistant_response),
        ):
            if not isinstance(content, str) or not content.strip():
                continue
            report["read"] += 1
            reference = f"{path.name}#session_id={session_id}&turn_index={turn_index}&role={role}"
            records.append(make_record(
                source="GitHub Copilot Chat local session store",
                session_id=session_id,
                event_type=event_type,
                origin="historical_import",
                reference=reference,
                timestamp=timestamp,
                content=content,
                is_summary=False,
                source_truncated=role == "assistant" and len(content) >= 5_000,
            ))
    report["accepted"] = len(records)
    return records, report


def gather_sources(
    paths: list[Path], *, recursive: bool = False, destination: Path | None = None
) -> tuple[list[Path], list[dict[str, Any]]]:
    files: list[Path] = []
    rejected: list[dict[str, Any]] = []
    for path in paths:
        if not path.exists():
            rejected.append({"file": str(path), "record": None, "reason": "source path does not exist"})
        elif path.is_symlink():
            rejected.append({"file": str(path), "record": None, "reason": "symbolic-link sources are not followed"})
        elif path.is_dir():
            root = path.resolve()
            children = path.rglob("*") if recursive else path.iterdir()
            for child in sorted(candidate for candidate in children if candidate.is_file()):
                resolved_child = child.resolve()
                if child.is_symlink() or not resolved_child.is_relative_to(root):
                    rejected.append({"file": str(child), "record": None, "reason": "symbolic-link or out-of-scope source is not followed"})
                elif destination and resolved_child == destination.resolve():
                    rejected.append({"file": str(child), "record": None, "reason": "canonical destination log is never an import source"})
                elif child.suffix.lower() in (".json", ".jsonl"):
                    files.append(child)
                else:
                    rejected.append({"file": str(child), "record": None, "reason": "unsupported source file; expected .json or .jsonl"})
            if not recursive:
                nested = sorted(candidate for candidate in path.iterdir() if candidate.is_dir())
                rejected.extend({"file": str(child), "record": None, "reason": "nested directory not traversed; pass --recursive to include it"} for child in nested)
        elif path.is_file():
            if destination and path.resolve() == destination.resolve():
                rejected.append({"file": str(path), "record": None, "reason": "canonical destination log is never an import source"})
            else:
                files.append(path)
    return files, rejected


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("sources", nargs="*", type=Path, help="Explicit .json/.jsonl files or directories to import")
    parser.add_argument("--log", type=Path, default=ROOT / "logs/ai-interactions.jsonl")
    parser.add_argument("--source-label", default="Historical AI interaction export")
    parser.add_argument("--sqlite-copilot-session", type=Path, help="Explicit local Copilot session-store.db file")
    parser.add_argument("--session-id", help="Required when importing one Copilot SQLite session")
    parser.add_argument("--expected-repository", help="Exact repository URL required to protect scope of SQLite imports")
    parser.add_argument("--strict", action="store_true", help="Return failure if any source record is rejected")
    parser.add_argument("--dry-run", action="store_true", help="Normalize and report without appending")
    parser.add_argument("--recursive", action="store_true", help="Opt in to recursively scanning explicitly supplied directories")
    args = parser.parse_args()

    if args.sqlite_copilot_session and (not args.session_id or not args.expected_repository):
        parser.error("--sqlite-copilot-session requires both --session-id and --expected-repository")
    if not args.sources and not args.sqlite_copilot_session:
        parser.error("provide one or more explicit source paths, or a scoped SQLite session")

    all_records: list[dict[str, Any]] = []
    reports: list[dict[str, Any]] = []
    if args.sources:
        paths, rejected_paths = gather_sources(args.sources, recursive=args.recursive, destination=args.log)
        reports.extend({"file": item["file"], "read": 0, "accepted": 0, "rejected": [item]} for item in rejected_paths)
        for path in paths:
            records, report = import_file(path, args.source_label)
            all_records.extend(records)
            reports.append(report)
    if args.sqlite_copilot_session:
        records, report = import_copilot_sqlite(args.sqlite_copilot_session, args.session_id, args.expected_repository)
        all_records.extend(records)
        reports.append(report)

    append_result = {"appended": 0, "duplicates": 0}
    if not args.dry_run and all_records:
        append_result = append_records(args.log, all_records)
    output = {
        "log": str(args.log),
        "dry_run": args.dry_run,
        "files": reports,
        "accepted_records": len(all_records),
        "appended_records": append_result["appended"],
        "duplicate_records": append_result["duplicates"],
        "rejected_records": sum(len(report.get("rejected", [])) for report in reports),
    }
    print(json.dumps(output, ensure_ascii=False, indent=2))
    return 1 if args.strict and output["rejected_records"] else 0


if __name__ == "__main__":
    raise SystemExit(main())
