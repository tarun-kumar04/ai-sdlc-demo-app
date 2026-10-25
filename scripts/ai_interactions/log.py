from __future__ import annotations

import hashlib
import json
import os
import re
import tempfile
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

if os.name == "nt":
    import msvcrt
else:
    import fcntl

REDACTED = "[REDACTED]"
CONTENT_LIMIT = 12_000
_SECRET_ASSIGNMENT = re.compile(
    r"(?i)(\b(?:password|passwd|secret|token|credential|api[_-]?key|access[_-]?token|refresh[_-]?token|authorization|cookie)\b\s*)(?:[:=]\s*)(\"[^\"]*\"|'[^']*'|[^\s,;]+)"
)
_BEARER = re.compile(r"(?i)\bBearer\s+[A-Za-z0-9._~+/-]+=*")
_TOKEN_SHAPE = re.compile(r"\b(?:gh[pousr]_[A-Za-z0-9_]{12,}|sk-[A-Za-z0-9_-]{16,})\b")
_DEMO_PASSWORD = re.compile(r"(?i)\bChangeMe[^\s`|,;]*")
_URL_CREDENTIALS = re.compile(r"(?i)(https?://)[^/@\s:]+:[^/@\s]+@")
METADATA_LIMIT = 1_024
SUPPORTED_EVENT_TYPES = {
    "session_start", "session_end", "prompt", "response", "tool_call", "tool_result",
    "tool_failure", "agent_start", "agent_result", "agent_stop", "error",
    "context_compaction", "documented_summary",
}

EVENT_FIELDS = {
    "SessionStart": ("session_start", "session_id", "source", "initial_prompt"),
    "SessionEnd": ("session_end", "session_id", "reason"),
    "UserPromptSubmit": ("prompt", "session_id", "prompt"),
    "PreToolUse": ("tool_call", "session_id", "tool_name", "tool_input"),
    "PostToolUse": ("tool_result", "session_id", "tool_name", "tool_input", "tool_result"),
    "PostToolUseFailure": ("tool_failure", "session_id", "tool_name", "tool_input", "error"),
    "SubagentStart": ("agent_start", "session_id", "agent_name", "agent_display_name", "agent_description"),
    "SubagentStop": ("agent_result", "session_id", "agent_name", "agent_display_name", "last_assistant_message", "stop_reason"),
    "Stop": ("agent_stop", "session_id", "stop_reason"),
    "ErrorOccurred": ("error", "session_id", "error", "error_context", "recoverable"),
    "PreCompact": ("context_compaction", "session_id", "trigger", "custom_instructions"),
}

EVENT_NAME_ALIASES = {
    "sessionStart": "SessionStart", "sessionEnd": "SessionEnd",
    "userPromptSubmitted": "UserPromptSubmit", "preToolUse": "PreToolUse",
    "postToolUse": "PostToolUse", "postToolUseFailure": "PostToolUseFailure",
    "subagentStart": "SubagentStart", "subagentStop": "SubagentStop",
    "agentStop": "Stop", "preCompact": "PreCompact",
}
FIELD_ALIASES = {
    "session_id": "sessionId", "initial_prompt": "initialPrompt",
    "tool_name": "toolName", "tool_input": "toolArgs", "tool_result": "toolResult",
    "agent_name": "agentName", "agent_display_name": "agentDisplayName",
    "agent_description": "agentDescription", "last_assistant_message": "response",
    "stop_reason": "stopReason", "error_context": "errorContext",
    "custom_instructions": "customInstructions",
}

def redact_text(value: str) -> tuple[str, bool]:
    redacted = _SECRET_ASSIGNMENT.sub(lambda match: match.group(1) + REDACTED, value)
    redacted = _BEARER.sub("Bearer " + REDACTED, redacted)
    redacted = _TOKEN_SHAPE.sub(REDACTED, redacted)
    redacted = _DEMO_PASSWORD.sub(REDACTED, redacted)
    redacted = _URL_CREDENTIALS.sub(r"\1[REDACTED]@", redacted)
    return redacted, redacted != value


def redact_value(value: Any) -> tuple[Any, bool]:
    if isinstance(value, str):
        return redact_text(value)
    if isinstance(value, list):
        output = []
        changed = False
        for item in value:
            redacted, item_changed = redact_value(item)
            output.append(redacted)
            changed |= item_changed
        return output, changed
    if isinstance(value, dict):
        output = {}
        changed = False
        for key, item in value.items():
            if re.search(r"(?i)(password|passwd|secret|token|credential|api[_-]?key|authorization|cookie)", str(key)):
                output[key] = REDACTED
                changed = True
            else:
                redacted, item_changed = redact_value(item)
                output[key] = redacted
                changed |= item_changed
        return output, changed
    return value, False


def normalize_timestamp(value: Any) -> tuple[str | None, str]:
    if value is None or value == "":
        return None, "unknown"
    try:
        if isinstance(value, bool):
            return None, "unknown"
        if isinstance(value, (int, float)):
            moment = datetime.fromtimestamp(value / 1000 if value > 10_000_000_000 else value, timezone.utc)
        else:
            moment = datetime.fromisoformat(str(value).replace("Z", "+00:00"))
            if moment.tzinfo is None:
                moment = moment.replace(tzinfo=timezone.utc)
        return moment.astimezone(timezone.utc).isoformat(timespec="milliseconds").replace("+00:00", "Z"), "known"
    except (ValueError, OverflowError, OSError, TypeError):
        return None, "unknown"


def stable_event_id(source: str, reference: str, event_type: str, content: Any) -> str:
    serialized = json.dumps(content, sort_keys=True, ensure_ascii=False, separators=(",", ":"))
    digest = hashlib.sha256(f"{source}\0{reference}\0{event_type}\0{serialized}".encode("utf-8")).hexdigest()
    return f"sha256:{digest}"


def _safe_metadata(value: Any) -> tuple[str, bool]:
    text = value if isinstance(value, str) else str(value)
    redacted, changed = redact_text(text)
    safe_value = _URL_CREDENTIALS.sub(r"\1[REDACTED]@", redacted)
    changed |= safe_value != redacted
    if len(safe_value) > METADATA_LIMIT:
        marker = "…[TRUNCATED]"
        safe_value = safe_value[: METADATA_LIMIT - len(marker)] + marker
        changed = True
    return safe_value, changed


def make_record(
    *,
    source: str,
    session_id: str | None,
    event_type: str,
    origin: str,
    reference: str,
    timestamp: Any,
    content: Any,
    is_summary: bool = False,
    source_truncated: bool = False,
) -> dict[str, Any]:
    if event_type not in SUPPORTED_EVENT_TYPES:
        raise ValueError(f"Unsupported interaction event type: {event_type}")
    if origin not in {"historical_import", "live_hook", "documented_summary"}:
        raise ValueError(f"Unsupported interaction origin: {origin}")
    if not isinstance(is_summary, bool):
        raise TypeError("is_summary must be a boolean")
    if origin == "documented_summary" and not is_summary:
        raise ValueError("documented_summary records must be labeled as summaries")
    safe_content, redacted = redact_value(content)
    safe_source, source_redacted = _safe_metadata(source)
    safe_reference, reference_redacted = _safe_metadata(reference)
    safe_session = None
    session_redacted = False
    if session_id:
        safe_session, session_redacted = _safe_metadata(session_id)
    redacted |= source_redacted or reference_redacted or session_redacted
    serialized = json.dumps(safe_content, ensure_ascii=False, sort_keys=True)
    truncated = source_truncated or len(serialized) > CONTENT_LIMIT
    if truncated:
        if len(serialized) > CONTENT_LIMIT:
            serialized = serialized[:CONTENT_LIMIT] + "…[TRUNCATED]"
    timestamp_text, timestamp_status = normalize_timestamp(timestamp)
    record: dict[str, Any] = {
        "event_id": stable_event_id(safe_source, safe_reference, event_type, safe_content),
        "timestamp": timestamp_text,
        "timestamp_status": timestamp_status,
        "source": safe_source,
        "event_type": event_type,
        "record_origin": origin,
        "content": serialized,
        "source_reference": safe_reference,
        "redaction_applied": redacted,
        "is_summary": is_summary,
    }
    if safe_session:
        record["session_id"] = safe_session
    if truncated:
        record["content_truncated"] = True
    return record


def append_records(path: str | Path, records: list[dict[str, Any]]) -> dict[str, int]:
    target = Path(path)
    target.parent.mkdir(parents=True, exist_ok=True)
    lock_key = hashlib.sha256(str(target.resolve()).encode("utf-8")).hexdigest()
    lock_path = Path(tempfile.gettempdir()) / f"ai-interactions-{lock_key}.lock"
    lock_path.touch(exist_ok=True)
    added = skipped = 0
    with lock_path.open("r+") as lock_file:
        if os.name == "nt":
            lock_file.seek(0)
            if not lock_file.read(1):
                lock_file.write("0")
                lock_file.flush()
            lock_file.seek(0)
            msvcrt.locking(lock_file.fileno(), msvcrt.LK_LOCK, 1)
        else:
            fcntl.flock(lock_file.fileno(), fcntl.LOCK_EX)
        existing_ids: set[str] = set()
        if target.exists():
            with target.open("r", encoding="utf-8") as current:
                for line in current:
                    try:
                        existing_ids.add(json.loads(line)["event_id"])
                    except (json.JSONDecodeError, KeyError, TypeError):
                        # Preserve malformed existing data; future records can still append.
                        continue
        with target.open("a", encoding="utf-8") as output:
            for record in records:
                if record["event_id"] in existing_ids:
                    skipped += 1
                    continue
                output.write(json.dumps(record, ensure_ascii=False, separators=(",", ":")) + "\n")
                existing_ids.add(record["event_id"])
                added += 1
            output.flush()
            os.fsync(output.fileno())
        if os.name == "nt":
            lock_file.seek(0)
            msvcrt.locking(lock_file.fileno(), msvcrt.LK_UNLCK, 1)
        else:
            fcntl.flock(lock_file.fileno(), fcntl.LOCK_UN)
    return {"appended": added, "duplicates": skipped}


def normalize_hook_event(payload: Any) -> dict[str, Any] | None:
    if not isinstance(payload, dict):
        return None
    event_name = payload.get("hook_event_name") or payload.get("event") or payload.get("eventName")
    if not isinstance(event_name, str):
        return None
    event_name = EVENT_NAME_ALIASES.get(event_name, event_name)
    event_type, session_key, *fields = EVENT_FIELDS.get(event_name, ("", "",))
    if not event_type:
        return None
    content = {}
    for field in fields:
        alias = FIELD_ALIASES.get(field)
        if field in payload:
            content[field] = payload[field]
        elif alias in payload:
            content[field] = payload[alias]
    session_id = payload.get(session_key) or payload.get("sessionId")
    timestamp = payload.get("timestamp")
    source = "VS Code GitHub Copilot" if "hook_event_name" in payload else "GitHub Copilot"
    reference = str(payload.get("event_id") or payload.get("eventId") or stable_event_id(source, str(session_id or "unknown-session"), event_type, {"timestamp": timestamp, "content": content}))
    return make_record(
        source=source,
        session_id=session_id if isinstance(session_id, str) else None,
        event_type=event_type,
        origin="live_hook",
        reference=reference,
        timestamp=timestamp,
        content=content,
    )
