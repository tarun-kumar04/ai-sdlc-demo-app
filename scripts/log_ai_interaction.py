#!/usr/bin/env python3
"""Copilot command-hook entry point: normalize one event from stdin to JSONL."""
from __future__ import annotations

import json
import os
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(Path(__file__).resolve().parent))
from ai_interactions.log import append_records, normalize_hook_event  # noqa: E402


def main() -> int:
    log_path = Path(os.environ.get("AI_INTERACTION_LOG_PATH", ROOT / "logs/ai-interactions.jsonl"))
    try:
        payload = json.load(sys.stdin)
        record = normalize_hook_event(payload)
        if record is None:
            print("AI interaction hook received an unsupported or malformed event; no record appended.", file=sys.stderr)
        else:
            append_records(log_path, [record])
    except (json.JSONDecodeError, OSError, TypeError, ValueError) as error:
        # Logging is best-effort so a corrupt or unavailable audit file does not interrupt the session.
        print(f"AI interaction logging skipped safely: {type(error).__name__}", file=sys.stderr)
    # Copilot expects a single JSON output object for command hooks. No decision is made.
    print("{}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
