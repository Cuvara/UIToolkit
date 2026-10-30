#!/usr/bin/env python3
"""Self-test for figma-import.py against an in-process mock of Pipeline's POST /api/exec.

The mock answers with the reply shapes connector 0.3.0-exp.1 and Pipeline 0.9.1-exp.1 produce
(FigmaRequestStore.Describe / Trim, FigmaRequestRefusal.ToReply, CommandExecutionResponse), so the
helper's polling, retries, exit codes and --save-result file are checked without Unity or Figma.

Run:  python3 Tools~/FigmaImport/test_figma_import.py      (Python 3.8+, standard library only)
"""

import contextlib
import importlib.util
import io
import json
import os
import shutil
import sys
import tempfile
import threading
import unittest
from datetime import datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler, HTTPServer

sys.dont_write_bytecode = True  # never leave __pycache__ in the package
HERE = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location("figma_import", os.path.join(HERE, "figma-import.py"))
helper = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(helper)

URL = "https://www.figma.com/design/AbC123/Shop-Demo?node-id=12-34"
TOKEN = "TOKEN"

DONE_RESULT = {
    "ok": True, "code": "", "error": "", "schemaMismatch": False, "converted": True, "ussErrors": False,
    "nodes": 42, "templates": 3, "uxml": "Assets/Figma/Shop-Demo/ShopScreen.uxml", "folder": "Assets/Figma/Shop-Demo",
    "files": ["Assets/Figma/Shop-Demo/ShopScreen.uxml", "Assets/Figma/Shop-Demo/ShopScreen.uss"],
    "removed": [], "invalidFiles": [],
    "facts": [{"kind": "MissingFonts", "severity": "warning", "count": 1, "items": ["Inter"]}],
    "inputWarnings": [], "elapsedMs": 1834,
}


def describe(state, **extra):
    reply = {"ok": state not in ("failed", "expired"), "requestId": "r-1a2b3c4d", "state": state,
             "fileName": "Shop Demo", "nodeId": "12:34", "elapsedSeconds": 1}
    if state not in ("done", "failed", "expired"):
        reply["retryAfterSeconds"] = 0.01
    reply.update(extra)
    return reply


class MockPipeline:
    """Answers figma_import_url once, then figma_import_status from a script of replies.

    Each script entry is either a reply dict (wrapped in a 200 success envelope) or an int HTTP
    status (answered with Pipeline's failure envelope)."""

    def __init__(self, queue_script, status_script):
        self.queue_script = list(queue_script)
        self.status_script = list(status_script)
        self.calls = []
        mock = self

        class Handler(BaseHTTPRequestHandler):
            def log_message(self, *args):
                pass

            def do_POST(self):
                body = json.loads(self.rfile.read(int(self.headers["Content-Length"])).decode("utf-8"))
                mock.calls.append({"path": self.path, "origin": self.headers.get("Origin"),
                                   "auth": self.headers.get("Authorization"), "body": body})
                if self.headers.get("Origin") is not None:
                    return self.answer(403, {"error": "Forbidden"})
                if self.headers.get("Authorization") != "Bearer " + TOKEN:
                    return self.answer(401, {"error": "Unauthorized"})
                script = mock.queue_script if body.get("command") == "figma_import_url" else mock.status_script
                step = script.pop(0) if len(script) > 1 else script[0]
                if isinstance(step, int):
                    return self.answer(step, {"success": False, "error": "Server Busy", "errorDetails": "settling",
                                              "status": "busy", "retryable": True})
                return self.answer(200, {"success": True, "result": step})

            def answer(self, code, payload):
                data = json.dumps(payload).encode("utf-8")
                self.send_response(code)
                self.send_header("Content-Type", "application/json")
                self.send_header("Content-Length", str(len(data)))
                self.end_headers()
                self.wfile.write(data)

        self.server = HTTPServer(("127.0.0.1", 0), Handler)
        self.port = self.server.server_address[1]
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)

    def __enter__(self):
        self.thread.start()
        return self

    def __exit__(self, *exc):
        self.server.shutdown()
        self.server.server_close()


class HelperTests(unittest.TestCase):
    def setUp(self):
        self.project = tempfile.mkdtemp(prefix="cuvara-figma-helper-")
        os.makedirs(os.path.join(self.project, "Library", "Pipeline"))
        self._busy = helper.BUSY_RETRY_SECONDS
        helper.BUSY_RETRY_SECONDS = 0

    def tearDown(self):
        helper.BUSY_RETRY_SECONDS = self._busy
        shutil.rmtree(self.project, ignore_errors=True)

    def write_port_file(self, port, heartbeat=None):
        stamp = heartbeat or datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.1234567Z")
        with open(os.path.join(self.project, helper.PORT_FILE), "w", encoding="utf-8") as handle:
            json.dump({"pid": 1, "port": port, "evalToken": TOKEN, "lastHeartbeat": stamp,
                       "endpoints": [{"type": "http", "port": str(port)}]}, handle)

    def run_helper(self, *extra):
        out, err = io.StringIO(), io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            code = helper.main([URL, "--project", self.project] + list(extra))
        return code, out.getvalue(), err.getvalue()

    def saved(self, name="last-import-Shop-Demo.json"):
        path = os.path.join(self.project, "Library", "CuvaraFigma", name)
        self.assertTrue(os.path.isfile(path), f"{path} was not written")
        with open(path, encoding="utf-8") as handle:
            return json.load(handle)

    def test_done_saves_the_result_with_the_request_fields(self):
        with MockPipeline([describe("queued")], [describe("exporting"), describe("done", result=DONE_RESULT)]) as mock:
            self.write_port_file(mock.port)
            code, out, err = self.run_helper()

        self.assertEqual(code, 0, err)
        document = self.saved()
        self.assertTrue(document["ok"])
        self.assertEqual(document["folder"], "Assets/Figma/Shop-Demo")
        self.assertEqual(document["facts"][0]["kind"], "MissingFonts")
        self.assertEqual(document["request"]["state"], "done")
        self.assertEqual(document["request"]["requestId"], "r-1a2b3c4d")
        self.assertEqual(document["request"]["fileName"], "Shop Demo")
        self.assertEqual(document["request"]["url"], URL)
        self.assertRegex(document["request"]["savedAt"], r"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ$")

        # The wire: bearer token, no Origin, Pipeline's millisecond timeout, headless by default.
        self.assertEqual([c["body"]["command"] for c in mock.calls],
                         ["figma_import_url", "figma_import_status", "figma_import_status"])
        self.assertTrue(all(c["origin"] is None and c["auth"] == "Bearer " + TOKEN for c in mock.calls))
        self.assertTrue(all(c["path"] == "/api/exec" for c in mock.calls))
        self.assertEqual(mock.calls[0]["body"]["parameters"], {"url": URL, "headless": True})
        self.assertEqual(mock.calls[0]["body"]["timeout"], helper.QUEUE_TIMEOUT_MS)
        self.assertEqual(mock.calls[1]["body"]["parameters"], {"request_id": "r-1a2b3c4d"})
        self.assertEqual(mock.calls[1]["body"]["timeout"], helper.STATUS_TIMEOUT_MS)

    def test_failed_import_with_a_result_saves_it_and_exits_1(self):
        result = dict(DONE_RESULT, ok=False, code="files_not_imported", ussErrors=True,
                      invalidFiles=["Assets/Figma/Shop-Demo/ShopScreen.uss"])
        failed = describe("failed", code="files_not_imported", error="Unity did not import every file.", result=result)
        with MockPipeline([describe("queued")], [failed]) as mock:
            self.write_port_file(mock.port)
            code, _, err = self.run_helper()

        self.assertEqual(code, 1, err)
        document = self.saved()
        self.assertFalse(document["ok"])
        self.assertEqual(document["code"], "files_not_imported")
        self.assertEqual(document["invalidFiles"], ["Assets/Figma/Shop-Demo/ShopScreen.uss"])
        self.assertEqual(document["request"]["state"], "failed")

    def test_refusal_before_queueing_saves_the_refusal_named_after_the_link(self):
        refusal = {"ok": False, "code": "plugin_not_connected", "error": "No UI Toolkit Connector plugin is connected.",
                   "project": "Lab"}
        with MockPipeline([refusal], [describe("done")]) as mock:
            self.write_port_file(mock.port)
            code, _, err = self.run_helper()

        self.assertEqual(code, 1, err)
        document = self.saved("last-import-Shop-Demo.json")
        self.assertEqual((document["ok"], document["code"]), (False, "plugin_not_connected"))
        self.assertEqual(document["request"]["fileName"], "Shop-Demo")
        self.assertEqual(document["request"]["state"], "")
        self.assertEqual(len(mock.calls), 1)

    def test_expired_request_is_saved_without_a_result(self):
        expired = describe("expired", code="expired", error="The plugin did not pick up the request within 30 s.")
        with MockPipeline([describe("queued")], [expired]) as mock:
            self.write_port_file(mock.port)
            code, _, err = self.run_helper()

        self.assertEqual(code, 1, err)
        document = self.saved()
        self.assertEqual((document["ok"], document["code"]), (False, "expired"))
        self.assertNotIn("result", document)

    def test_max_wait_timeout_saves_the_pending_request_as_unfinished(self):
        with MockPipeline([describe("queued")], [describe("exporting")]) as mock:
            self.write_port_file(mock.port)
            code, _, err = self.run_helper("--max-wait", "0.3")

        self.assertEqual(code, 1, err)
        self.assertIn("still 'exporting'", err)
        document = self.saved()
        self.assertFalse(document["ok"])
        self.assertNotIn("result", document)
        self.assertEqual(document.get("code", ""), "")
        self.assertEqual(document["request"]["state"], "exporting")
        self.assertIs(document["request"]["finished"], False)
        self.assertEqual(document["request"]["requestId"], "r-1a2b3c4d")
        self.assertEqual(document["request"]["waitedSeconds"], 0.3)

    def test_finished_requests_are_saved_as_finished(self):
        with MockPipeline([describe("queued")], [describe("done", result=DONE_RESULT)]) as mock:
            self.write_port_file(mock.port)
            self.run_helper()
        self.assertIs(self.saved()["request"]["finished"], True)

    def test_busy_and_too_many_commands_are_retried(self):
        with MockPipeline([503, 429, 504, describe("queued")], [describe("done", result=DONE_RESULT)]) as mock:
            self.write_port_file(mock.port)
            code, _, err = self.run_helper()

        self.assertEqual(code, 0, err)
        self.assertEqual([c["body"]["command"] for c in mock.calls].count("figma_import_url"), 4)
        self.assertIn("HTTP 429", err)

    def test_rejected_token_is_exit_2_and_saves_nothing(self):
        with MockPipeline([describe("queued")], [describe("done", result=DONE_RESULT)]) as mock:
            self.write_port_file(mock.port)
            with open(os.path.join(self.project, helper.PORT_FILE), "w", encoding="utf-8") as handle:
                json.dump({"port": mock.port, "evalToken": "WRONG"}, handle)
            code, _, err = self.run_helper()

        self.assertEqual(code, 2)
        self.assertIn("401", err)
        self.assertFalse(os.path.exists(os.path.join(self.project, "Library", "CuvaraFigma")))

    def test_save_result_path_and_no_save_result(self):
        custom = os.path.join(self.project, "out", "result.json")
        with MockPipeline([describe("queued")], [describe("done", result=DONE_RESULT)]) as mock:
            self.write_port_file(mock.port)
            code, _, err = self.run_helper("--save-result", custom)
            self.assertEqual(code, 0, err)
            with open(custom, encoding="utf-8") as handle:
                self.assertTrue(json.load(handle)["ok"])
            self.assertFalse(os.path.exists(os.path.join(self.project, "Library", "CuvaraFigma")))

            code, _, err = self.run_helper("--no-save-result")
            self.assertEqual(code, 0, err)
            self.assertFalse(os.path.exists(os.path.join(self.project, "Library", "CuvaraFigma")))

    def test_dry_run_sends_and_saves_nothing(self):
        code, out, _ = self.run_helper("--dry-run")
        self.assertEqual(code, 0)
        self.assertIn('"command": "figma_import_url"', out)
        self.assertFalse(os.path.exists(os.path.join(self.project, "Library", "CuvaraFigma")))

    def test_a_stale_heartbeat_warns(self):
        old = (datetime.now(timezone.utc) - timedelta(minutes=5)).strftime("%Y-%m-%dT%H:%M:%S.1234567Z")
        with MockPipeline([describe("queued")], [describe("done", result=DONE_RESULT)]) as mock:
            self.write_port_file(mock.port, heartbeat=old)
            code, _, err = self.run_helper("--no-save-result")
        self.assertEqual(code, 0, err)
        self.assertIn("heartbeat is", err)


class UnitTests(unittest.TestCase):
    def test_parse_heartbeat_reads_newtonsoft_seven_digit_utc(self):
        seen = helper.parse_heartbeat("2026-10-30T15:22:14.1234567Z")
        self.assertEqual(seen, datetime(2026, 10, 30, 15, 22, 14, 123456, tzinfo=timezone.utc))
        self.assertEqual(helper.parse_heartbeat("2026-10-30T15:22:14").tzinfo, timezone.utc)
        self.assertIsNone(helper.parse_heartbeat("not a date"))
        self.assertIsNone(helper.parse_heartbeat(None))

    def test_sanitize_name_matches_the_connector_rule(self):
        self.assertEqual(helper.sanitize_name("Shop Demo (v2)"), "Shop-Demo--v2")
        self.assertEqual(helper.sanitize_name("ui_kit-1"), "ui_kit-1")
        self.assertEqual(helper.sanitize_name("***"), "import")
        self.assertEqual(helper.sanitize_name(None), "import")

    def test_figma_file_name_reads_the_link(self):
        self.assertEqual(helper.figma_file_name(URL), "Shop-Demo")
        self.assertEqual(helper.figma_file_name("https://www.figma.com/design/AbC123/My%20File?node-id=1-2"), "My File")
        self.assertEqual(helper.figma_file_name("https://www.figma.com/design/AbC123?node-id=1-2"), "AbC123")


if __name__ == "__main__":
    unittest.main(verbosity=2)
