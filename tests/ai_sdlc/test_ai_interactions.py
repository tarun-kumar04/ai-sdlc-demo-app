from __future__ import annotations

import json
import os
import sqlite3
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))
sys.path.insert(0, str(ROOT / "scripts" / "ai_interactions"))

from ai_interactions.log import append_records, make_record, normalize_hook_event
from import_ai_interactions import gather_sources, import_copilot_sqlite, import_file
from validate_structured_report import validate


class InteractionLogTests(unittest.TestCase):
    def test_valid_hook_event_is_normalized_redacted_and_appended(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            log_path = Path(directory) / "events.jsonl"
            event = {
                "hook_event_name": "UserPromptSubmit",
                "session_id": "session-1",
                "timestamp": "2026-10-24T18:00:00Z",
                "cwd": "/workspace/project",
                "prompt": "Login with password=synthetic-fixture-secret and Bearer abcdefghijklmnopqrstuvwxyz123456",
                "environment": {"TOKEN": "not-to-log"},
            }

            record = normalize_hook_event(event)
            self.assertIsNotNone(record)
            counts = append_records(log_path, [record])
            stored = json.loads(log_path.read_text(encoding="utf-8").splitlines()[0])

            self.assertEqual("prompt", stored["event_type"])
            self.assertEqual("live_hook", stored["record_origin"])
            self.assertEqual("known", stored["timestamp_status"])
            self.assertTrue(stored["redaction_applied"])
            self.assertNotIn("synthetic-fixture-secret", stored["content"])
            self.assertNotIn("abcdefghijklmnopqrstuvwxyz123456", stored["content"])
            self.assertNotIn("environment", stored["content"])
            self.assertEqual({"appended": 1, "duplicates": 0}, counts)

    def test_second_event_appends_and_retry_is_deduplicated(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            log_path = Path(directory) / "events.jsonl"
            first = make_record(source="fixture", session_id="s", event_type="prompt", origin="live_hook",
                                reference="event-1", timestamp=None, content="first")
            second = make_record(source="fixture", session_id="s", event_type="tool_call", origin="live_hook",
                                 reference="event-2", timestamp=1792864800000, content={"tool": "read"})

            self.assertEqual({"appended": 1, "duplicates": 0}, append_records(log_path, [first]))
            self.assertEqual({"appended": 1, "duplicates": 0}, append_records(log_path, [second]))
            self.assertEqual({"appended": 0, "duplicates": 1}, append_records(log_path, [first]))
            rows = [json.loads(line) for line in log_path.read_text(encoding="utf-8").splitlines()]
            self.assertEqual(2, len(rows))
            self.assertEqual("unknown", rows[0]["timestamp_status"])
            self.assertIsNone(rows[0]["timestamp"])
            self.assertEqual("known", rows[1]["timestamp_status"])

    def test_boolean_timestamps_are_marked_unknown(self) -> None:
        false_record = make_record(source="fixture", session_id=None, event_type="prompt", origin="live_hook",
                                   reference="false-time", timestamp=False, content="hello")
        true_record = make_record(source="fixture", session_id=None, event_type="prompt", origin="live_hook",
                                  reference="true-time", timestamp=True, content="hello")

        self.assertEqual("unknown", false_record["timestamp_status"])
        self.assertIsNone(false_record["timestamp"])
        self.assertEqual("unknown", true_record["timestamp_status"])
        self.assertIsNone(true_record["timestamp"])

    def test_unknown_or_malformed_hook_payload_is_ignored_safely(self) -> None:
        self.assertIsNone(normalize_hook_event(None))
        self.assertIsNone(normalize_hook_event({"hook_event_name": "UnknownEvent"}))
        self.assertIsNone(normalize_hook_event({"hook_event_name": 42}))

    def test_camel_case_hook_payload_and_metadata_secrets_are_normalized(self) -> None:
        event = {
            "eventName": "preToolUse",
            "sessionId": "session-token=fixture-secret",
            "timestamp": "2026-10-24T18:00:00Z",
            "toolName": "read_file",
            "toolArgs": {"url": "https://alice:secret@example.test/data", "nested": {"token": "value"}},
        }

        record = normalize_hook_event(event)

        self.assertIsNotNone(record)
        self.assertEqual("tool_call", record["event_type"])
        self.assertNotIn("fixture-secret", record["session_id"])
        self.assertIn("[REDACTED]", record["session_id"])
        self.assertNotIn("alice:secret", record["content"])
        self.assertNotIn('"token": "value"', record["content"])
        self.assertTrue(record["redaction_applied"])

    def test_stop_hook_does_not_store_local_transcript_path(self) -> None:
        record = normalize_hook_event({
            "hook_event_name": "Stop", "session_id": "fixture-session",
            "transcript_path": "/Users/private-user/Library/Application Support/chat.jsonl",
            "stop_reason": "completed",
        })

        self.assertIsNotNone(record)
        self.assertNotIn("private-user", record["content"])
        self.assertNotIn("transcript_path", record["content"])

    def test_metadata_is_redacted_and_bounded(self) -> None:
        record = make_record(
            source="source token=fixture-secret",
            session_id="s" * 2_000,
            event_type="prompt",
            origin="historical_import",
            reference="https://user:password@example.test/" + "x" * 2_000,
            timestamp=None,
            content="safe",
        )

        self.assertNotIn("fixture-secret", record["source"])
        self.assertNotIn("user:password", record["source_reference"])
        self.assertLessEqual(len(record["source"]), 1_024)
        self.assertLessEqual(len(record["source_reference"]), 1_024)
        self.assertLessEqual(len(record["session_id"]), 1_024)
        self.assertTrue(record["redaction_applied"])

    def test_record_constructor_enforces_schema_invariants(self) -> None:
        arguments = {
            "source": "fixture", "session_id": None, "event_type": "prompt",
            "origin": "historical_import", "reference": "event", "timestamp": None,
            "content": "hello",
        }
        with self.assertRaises(TypeError):
            make_record(**arguments, is_summary="false")
        with self.assertRaises(ValueError):
            make_record(**{**arguments, "event_type": "arbitrary"})
        with self.assertRaises(ValueError):
            make_record(**{**arguments, "origin": "documented_summary"})

    def test_hook_command_appends_jsonl_and_handles_malformed_input(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            log_path = Path(directory) / "hook.jsonl"
            environment = {**os.environ, "AI_INTERACTION_LOG_PATH": str(log_path)}
            script = ROOT / "scripts" / "log_ai_interaction.py"
            event = {"hook_event_name": "SessionEnd", "session_id": "fixture-session",
                     "timestamp": "2026-10-24T19:00:00Z", "cwd": "/workspace", "reason": "complete"}

            first = subprocess.run([sys.executable, str(script)], input=json.dumps(event), text=True,
                                   capture_output=True, env=environment, check=False)
            second = subprocess.run([sys.executable, str(script)], input=json.dumps(event), text=True,
                                    capture_output=True, env=environment, check=False)
            malformed = subprocess.run([sys.executable, str(script)], input="not json", text=True,
                                       capture_output=True, env=environment, check=False)

            self.assertEqual(0, first.returncode)
            self.assertEqual("{}", first.stdout.strip())
            self.assertEqual(0, second.returncode)
            self.assertEqual("{}", second.stdout.strip())
            self.assertEqual(0, malformed.returncode)
            self.assertEqual("{}", malformed.stdout.strip())
            self.assertEqual(1, len(log_path.read_text(encoding="utf-8").splitlines()))

    def test_historical_jsonl_is_idempotent_and_summary_is_labeled(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / "history.jsonl"
            log_path = Path(directory) / "events.jsonl"
            source.write_text(
                "\n".join([
                    json.dumps({"id": "turn-1", "role": "user", "timestamp": "2026-10-23T12:00:00Z", "content": "Implement the approval flow."}),
                    json.dumps({"id": "turn-2", "role": "assistant", "timestamp": None, "content": "Summary: approval tests passed.", "is_summary": True}),
                ]) + "\n",
                encoding="utf-8",
            )
            records, report = import_file(source, "fixture export")
            self.assertEqual(2, report["accepted"])
            self.assertEqual([], report["rejected"])
            first = append_records(log_path, records)
            second, _ = import_file(source, "fixture export")
            repeated = append_records(log_path, second)

            self.assertEqual(2, first["appended"])
            self.assertEqual(2, repeated["duplicates"])
            rows = [json.loads(line) for line in log_path.read_text(encoding="utf-8").splitlines()]
            self.assertEqual(["historical_import", "documented_summary"], [row["record_origin"] for row in rows])
            self.assertTrue(rows[1]["is_summary"])
            self.assertEqual("unknown", rows[1]["timestamp_status"])

    def test_chatgpt_export_shape_normalizes_messages(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / "conversation.json"
            source.write_text(json.dumps({
                "mapping": {
                    "node-user": {"message": {"id": "message-user", "author": {"role": "user"}, "create_time": 1_792_777_200, "content": {"parts": ["hello", 12]}}},
                    "node-assistant": {"message": {"author": {"role": "assistant"}, "create_time": None, "content": {"parts": ["world"]}}},
                }
            }), encoding="utf-8")

            records, report = import_file(source, "fixture")

            self.assertEqual(2, report["accepted"])
            self.assertEqual(["prompt", "response"], [record["event_type"] for record in records])
            self.assertEqual("ChatGPT data export", records[0]["source"])
            self.assertEqual("known", records[0]["timestamp_status"])
            self.assertEqual("unknown", records[1]["timestamp_status"])

    def test_directory_import_reports_unsupported_files(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "supported.json").write_text("[]", encoding="utf-8")
            (root / "unsupported.txt").write_text("transcript", encoding="utf-8")

            files, rejected = gather_sources([root])

            self.assertEqual([root / "supported.json"], files)
            self.assertEqual(1, len(rejected))
            self.assertEqual(root / "unsupported.txt", Path(rejected[0]["file"]))

    def test_directory_import_is_nonrecursive_and_excludes_destination(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            destination = root / "events.jsonl"
            destination.write_text("{}\n", encoding="utf-8")
            nested = root / "nested"
            nested.mkdir()
            nested_file = nested / "private.json"
            nested_file.write_text("[]", encoding="utf-8")
            direct = root / "approved.json"
            direct.write_text("[]", encoding="utf-8")

            files, rejected = gather_sources([root], destination=destination)
            recursive_files, recursive_rejected = gather_sources([root], recursive=True, destination=destination)

            self.assertEqual([direct], files)
            self.assertEqual([direct, nested_file], recursive_files)
            self.assertEqual(2, len(rejected))
            self.assertEqual(1, len(recursive_rejected))

    def test_directory_import_rejects_symlinked_external_file(self) -> None:
        with tempfile.TemporaryDirectory() as directory, tempfile.TemporaryDirectory() as outside:
            root = Path(directory)
            external = Path(outside) / "private.json"
            external.write_text("[]", encoding="utf-8")
            link = root / "linked.json"
            try:
                link.symlink_to(external)
            except (OSError, NotImplementedError):
                self.skipTest("symlink creation is unavailable")

            files, rejected = gather_sources([root])

            self.assertEqual([], files)
            self.assertEqual(1, len(rejected))
            self.assertIn("symbolic-link", rejected[0]["reason"])

    def test_import_rejects_non_boolean_summary_and_unknown_event_type(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / "events.jsonl"
            source.write_text("\n".join([
                json.dumps({"role": "assistant", "content": "summary", "is_summary": "false"}),
                json.dumps({"event_type": "arbitrary", "content": "not an event"}),
            ]) + "\n", encoding="utf-8")

            records, report = import_file(source, "fixture")

            self.assertEqual([], records)
            self.assertEqual(2, len(report["rejected"]))
            self.assertIn("JSON boolean", report["rejected"][0]["reason"])
            self.assertIn("unsupported event type", report["rejected"][1]["reason"])

    def test_import_cli_dry_run_and_strict_rejection(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / "source.jsonl"
            source.write_text(json.dumps({"role": "user", "content": "fixture prompt"}) + "\n{bad}\n", encoding="utf-8")
            log_path = root / "canonical.jsonl"
            script = ROOT / "scripts" / "import_ai_interactions.py"
            dry_run = subprocess.run(
                [sys.executable, str(script), str(source), "--log", str(log_path), "--dry-run"],
                text=True, capture_output=True, check=False,
            )
            self.assertEqual(0, dry_run.returncode)
            self.assertFalse(log_path.exists())
            self.assertEqual(1, json.loads(dry_run.stdout)["accepted_records"])
            strict = subprocess.run(
                [sys.executable, str(script), str(source), "--log", str(log_path), "--strict"],
                text=True, capture_output=True, check=False,
            )

            self.assertEqual(1, strict.returncode)
            self.assertEqual(1, json.loads(strict.stdout)["appended_records"])

    def test_malformed_and_unsupported_export_records_are_reported(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            malformed = Path(directory) / "malformed.jsonl"
            malformed.write_text("{not json}\n{}\n", encoding="utf-8")
            _, report = import_file(malformed, "fixture")
            self.assertEqual(2, report["read"])
            self.assertEqual(2, len(report["rejected"]))

            unsupported = Path(directory) / "transcript.txt"
            unsupported.write_text("text transcript", encoding="utf-8")
            _, unsupported_report = import_file(unsupported, "fixture")
            self.assertIn("unsupported file extension", unsupported_report["rejected"][0]["reason"])

    def test_scoped_copilot_sqlite_import_preserves_timestamps_and_redacts(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            database_path = Path(directory) / "session-store.db"
            with sqlite3.connect(database_path) as database:
                database.execute("CREATE TABLE sessions (id TEXT PRIMARY KEY, repository TEXT)")
                database.execute("CREATE TABLE turns (session_id TEXT, turn_index INTEGER, timestamp TEXT, user_message TEXT, assistant_response TEXT)")
                database.execute("INSERT INTO sessions VALUES (?, ?)", ("wanted-session", "https://example.test/ai-sdlc-demo-app.git"))
                database.execute("INSERT INTO sessions VALUES (?, ?)", ("other-session", "https://example.test/other.git"))
                database.execute("INSERT INTO turns VALUES (?, ?, ?, ?, ?)",
                                 ("wanted-session", 0, "2026-10-23T12:00:00Z", "Use password=synthetic-fixture-secret", "I will inspect the repo."))
                database.execute("INSERT INTO turns VALUES (?, ?, ?, ?, ?)",
                                 ("other-session", 0, "2026-10-23T12:00:00Z", "private other chat", "unrelated"))

            records, report = import_copilot_sqlite(
                database_path, "wanted-session", "https://example.test/ai-sdlc-demo-app.git"
            )
            self.assertEqual(2, report["accepted"])
            self.assertTrue(all(record["timestamp_status"] == "known" for record in records))
            self.assertTrue(all(record["record_origin"] == "historical_import" for record in records))
            self.assertIn("[REDACTED]", records[0]["content"])
            self.assertNotIn("synthetic-fixture-secret", records[0]["content"])

            denied_records, denied_report = import_copilot_sqlite(
                database_path, "other-session", "https://example.test/ai-sdlc-demo-app.git"
            )
            self.assertEqual([], denied_records)
            self.assertIn("does not match", denied_report["rejected"][0]["reason"])

    def test_sqlite_assistant_response_at_store_limit_is_marked_truncated(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            database_path = Path(directory) / "session-store.db"
            with sqlite3.connect(database_path) as database:
                database.execute("CREATE TABLE sessions (id TEXT PRIMARY KEY, repository TEXT)")
                database.execute("CREATE TABLE turns (session_id TEXT, turn_index INTEGER, timestamp TEXT, user_message TEXT, assistant_response TEXT)")
                database.execute("INSERT INTO sessions VALUES (?, ?)", ("session", "https://example.test/repo.git"))
                database.execute("INSERT INTO turns VALUES (?, ?, ?, ?, ?)",
                                 ("session", 0, "2026-10-23T12:00:00Z", None, "x" * 5_000))

            records, _ = import_copilot_sqlite(database_path, "session", "https://example.test/repo.git")

            self.assertEqual(1, len(records))
            self.assertEqual("response", records[0]["event_type"])
            self.assertTrue(records[0]["content_truncated"])

    def test_report_validator_requires_dispositions_and_reasons(self) -> None:
        valid = {
            "schema_version": 1,
            "report_type": "code-review",
            "created_at": "2026-10-24T00:00:00Z",
            "summary": "Reviewed the change.",
            "findings": [{
                "id": "R-1", "severity": "low", "title": "Example", "file": "src/example.cs",
                "line": 5, "evidence": "Observed source", "impact": "Limited", "recommendation": "Consider change",
                "verdict": "dismissed", "verdict_reason": "Not a defect in scope"
            }],
            "checks": [],
            "sources": []
        }
        self.assertEqual([], validate(valid))
        valid["findings"][0]["verdict_reason"] = ""
        self.assertTrue(any("verdict_reason" in error for error in validate(valid)))
        valid["findings"][0]["severity"] = []
        valid["schema_version"] = True
        valid["created_at"] = "not a date"
        self.assertTrue(validate(valid))

    def test_interaction_record_validator_accepts_valid_and_rejects_invalid_types(self) -> None:
        valid = make_record(source="fixture", session_id="s", event_type="prompt", origin="live_hook",
                            reference="event", timestamp="2026-10-24T18:00:00Z", content="hello")
        self.assertEqual([], validate(valid, "interaction"))
        valid["timestamp"] = []
        valid["redaction_applied"] = "false"
        valid["event_type"] = []
        self.assertTrue(validate(valid, "interaction"))
        valid = make_record(source="fixture", session_id=None, event_type="prompt", origin="live_hook",
                    reference="event", timestamp=None, content="hello")
        valid["unexpected"] = "not in the record schema"
        self.assertTrue(any("unexpected interaction field" in error for error in validate(valid, "interaction")))


if __name__ == "__main__":
    unittest.main()
