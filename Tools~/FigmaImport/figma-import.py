#!/usr/bin/env python3
"""Headless Figma frame import through Unity's UI Toolkit Connector for Figma.

Thin client for two Pipeline commands that com.unity.ui.figma (0.3.0-exp.1) registers:
``figma_import_url`` queues an import of one Figma frame link, and ``figma_import_status``
reports where the request is. The connector does the import; this script only calls it,
waits, and prints the result. It never edits files in the import folder.

Requirements:
  * The Unity Editor is open on the project with com.unity.ui.figma installed
    (Pipeline writes Library/Pipeline/.unity-pipeline-port).
  * The "Unity UI Toolkit Connector" plugin is open in Figma on the same file and
    connected to this Editor. The plugin does the export; without it the request
    expires after about 30 s.

Result file: the final import result (the connector's FigmaImportResult, or the refusal for a
request it never queued) is saved as JSON to <project>/Library/CuvaraFigma/last-import-<file>.json
(--save-result to choose the path, --no-save-result to skip). The Figma Diagnostics window reads
it, because a headless import leaves no other record of its notes (missing fonts, images that
never arrived, dropped features).

Exit codes (same as the UxmlCodegenCli validate verb):
  0  import done and ok (and, with --validate, no validator errors)
  1  import failed or expired, the import result is not ok, or the validator found errors
  2  usage, port file, connection or protocol problem

Python 3.8+, standard library only. See Documentation~/FIGMA-VALIDATION.md.
"""

import argparse
import json
import os
import re
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime, timezone

PORT_FILE = os.path.join("Library", "Pipeline", ".unity-pipeline-port")
RESULT_FOLDER = os.path.join("Library", "CuvaraFigma")
RESULT_PREFIX = "last-import-"
FINISHED_STATES = ("done", "failed", "expired")
HEARTBEAT_STALE_SECONDS = 60
# figma_import_status waits behind a running import; Pipeline's timeout is in milliseconds.
STATUS_TIMEOUT_MS = 120000
QUEUE_TIMEOUT_MS = 60000
BUSY_RETRY_SECONDS = 5
# 503: Editor settling or blocked by a dialog. 504: timed out in the queue. 429: too many queued
# commands. Pipeline runs nothing for any of them, and the same link returns the same request.
RETRY_STATUSES = (429, 503, 504)


class HelperError(Exception):
    """A problem that ends the run with exit code 2."""


def find_project_root(start):
    """Walks up from start to the folder that holds Library/Pipeline/.unity-pipeline-port."""
    directory = os.path.abspath(start)
    while True:
        if os.path.isfile(os.path.join(directory, PORT_FILE)):
            return directory
        parent = os.path.dirname(directory)
        if parent == directory:
            return None
        directory = parent


def parse_heartbeat(text):
    """Pipeline's lastHeartbeat (a Newtonsoft DateTime, e.g. 2026-10-30T15:22:14.1234567Z) as an
    aware UTC datetime, or None. Python before 3.11 reads at most 6 fraction digits, so the 7th is cut."""
    if not isinstance(text, str) or not text:
        return None
    value = text.strip().replace("Z", "+00:00")
    value = re.sub(r"(\.\d{6})\d+", r"\1", value)
    try:
        seen = datetime.fromisoformat(value)
    except ValueError:
        return None
    if seen.tzinfo is None:
        seen = seen.replace(tzinfo=timezone.utc)  # Pipeline writes DateTime.UtcNow
    return seen


def sanitize_name(name):
    """The connector's file-name rule (FigmaPathGuard.SanitizeName): anything outside [A-Za-z0-9_-]
    becomes '-', outer dashes trimmed. FigmaImportResultReader.SanitizeName is the C# twin."""
    result = re.sub(r"[^A-Za-z0-9_-]", "-", name or "").strip("-")
    return result or "import"


def figma_file_name(url):
    """The file name a Figma link carries (/design/<key>/<name>), or the file key, or ''."""
    try:
        parts = [p for p in urllib.parse.urlparse(url).path.split("/") if p]
    except ValueError:
        return ""
    if len(parts) >= 3:
        return urllib.parse.unquote(parts[2])
    return parts[1] if len(parts) >= 2 else ""


def result_document(reply, url, waited=None):
    """What --save-result writes: the connector's import result when the reply has one (a
    FigmaImportResult as figma_import_status returns it), else the reply itself (a refusal or an
    expired request: ok, code, error). A "request" object adds what only the helper knows.

    A request still open when --max-wait ran out (state queued, exporting or importing) is saved
    with ok false and no code: no import result exists yet, and request.finished is false."""
    result = reply.get("result")
    document = dict(result) if isinstance(result, dict) else {
        key: reply[key] for key in ("ok", "code", "error") if key in reply}
    finished = reply.get("state") in FINISHED_STATES or reply.get("state") is None
    if not finished:
        document["ok"] = False
    document.setdefault("ok", False)
    document["request"] = {
        "requestId": reply.get("requestId", ""),
        "state": reply.get("state", ""),
        "code": reply.get("code", ""),
        "error": reply.get("error", ""),
        "reason": reply.get("reason", ""),
        "fileName": reply.get("fileName") or figma_file_name(url),
        "nodeId": reply.get("nodeId", ""),
        "url": url,
        "savedAt": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "helper": "figma-import.py",
        "finished": finished,
    }
    if waited is not None:
        document["request"]["waitedSeconds"] = round(float(waited), 1)
    return document


def save_result(path, document):
    """Writes the result JSON (UTF-8, atomic replace). A failure is a warning: the import itself happened."""
    try:
        os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
        temp = path + ".tmp"
        with open(temp, "w", encoding="utf-8", newline="\n") as handle:
            json.dump(document, handle, indent=2, ensure_ascii=False)
            handle.write("\n")
        os.replace(temp, path)
        print(f"result saved to {path}", file=sys.stderr)
    except OSError as error:
        print(f"warning: cannot save the result to {path}: {error}", file=sys.stderr)


def default_result_path(project_root, document):
    name = sanitize_name(document.get("request", {}).get("fileName", ""))
    return os.path.join(project_root, RESULT_FOLDER, f"{RESULT_PREFIX}{name}.json")


def read_port_file(project_root):
    path = os.path.join(project_root, PORT_FILE)
    try:
        with open(path, "r", encoding="utf-8-sig") as handle:
            descriptor = json.load(handle)
    except (OSError, ValueError) as error:
        raise HelperError(f"cannot read {path}: {error}")

    port = descriptor.get("port") or 0
    if not port:
        for endpoint in descriptor.get("endpoints") or []:
            if endpoint.get("type") == "http" and endpoint.get("port"):
                port = endpoint["port"]
                break
    try:
        port = int(port)
    except (TypeError, ValueError):
        port = 0
    token = descriptor.get("evalToken")
    if not port or not token:
        raise HelperError(f"{path} has no port or evalToken; is the Pipeline server running?")

    seen = parse_heartbeat(descriptor.get("lastHeartbeat"))
    if seen is not None:
        age = (datetime.now(timezone.utc) - seen).total_seconds()
        if age > HEARTBEAT_STALE_SECONDS:
            print(f"warning: Pipeline heartbeat is {int(age)} s old; the Editor may be closed", file=sys.stderr)
    if descriptor.get("info"):
        print(f"note: {descriptor['info']}", file=sys.stderr)
    return port, token


def redact(body):
    return json.dumps(body, indent=2)


class Pipeline:
    def __init__(self, port, token, dry_run):
        self.url = f"http://127.0.0.1:{port}/api/exec"
        self.token = token
        self.dry_run = dry_run

    def exec(self, command, parameters, timeout_ms, deadline):
        """POST /api/exec and return the command's result object. Retries 503/504."""
        body = {"command": command, "parameters": parameters, "timeout": timeout_ms}
        if self.dry_run:
            print(f"POST {self.url}")
            print("Authorization: Bearer <evalToken from the port file>")
            print("Content-Type: application/json")
            print(redact(body))
            return None

        data = json.dumps(body).encode("utf-8")
        while True:
            # urllib sends no Origin header; Pipeline refuses any request that has one.
            request = urllib.request.Request(
                self.url,
                data=data,
                method="POST",
                headers={"Authorization": f"Bearer {self.token}", "Content-Type": "application/json"},
            )
            try:
                with urllib.request.urlopen(request, timeout=timeout_ms / 1000.0 + 15) as response:
                    envelope = json.loads(response.read().decode("utf-8"))
            except urllib.error.HTTPError as error:
                text = error.read().decode("utf-8", "replace")
                if error.code in RETRY_STATUSES and time.monotonic() < deadline:
                    print(f"pipeline busy (HTTP {error.code}), retrying in {BUSY_RETRY_SECONDS} s", file=sys.stderr)
                    time.sleep(BUSY_RETRY_SECONDS)
                    continue
                if error.code == 401:
                    raise HelperError("HTTP 401: the evalToken was rejected (Editor restarted?); retry to reread the port file")
                raise HelperError(f"{command}: HTTP {error.code}: {text[:2000]}")
            except (urllib.error.URLError, OSError) as error:
                raise HelperError(f"cannot reach Pipeline at {self.url}: {error}")
            except ValueError as error:
                raise HelperError(f"{command}: reply is not JSON: {error}")

            if not envelope.get("success"):
                raise HelperError(f"{command} failed: {envelope.get('error')}: {envelope.get('errorDetails')}")
            result = envelope.get("result")
            if not isinstance(result, dict):
                raise HelperError(f"{command}: unexpected result {json.dumps(result)[:500]}")
            return result


def run_validator(args, project_root, folder):
    """Runs the UxmlCodegenCli validate verb on the import folder; returns its exit code."""
    absolute = folder if os.path.isabs(folder) else os.path.join(project_root, folder)
    command = list(args.cli) if args.cli else [
        "dotnet", "run", "--project",
        os.path.join(os.path.dirname(os.path.abspath(__file__)), os.pardir, "UxmlCodegenCli"), "--",
    ]
    command += ["validate", absolute]
    if args.prefix:
        command += ["--prefix", args.prefix]
    if args.json:
        command += ["--json"]
    print("+ " + " ".join(command), file=sys.stderr, flush=True)
    try:
        return subprocess.call(command)
    except OSError as error:
        raise HelperError(f"cannot run the validator: {error}")


def parse_args(argv):
    parser = argparse.ArgumentParser(
        description="Import one Figma frame into a Unity project through the UI Toolkit Connector for Figma, headless.")
    parser.add_argument("url", help="Figma frame link (Copy link to selection), with ?node-id=")
    parser.add_argument("--project", default=".", help="Unity project folder, or any folder inside it (default: current)")
    parser.add_argument("--output-folder", help="project folder for the file's FIRST import, such as Assets/UI/Shop; "
                        "later imports of the file go where the first one went")
    parser.add_argument("--no-headless", action="store_true",
                        help="also ping the Project window and update the connector window's report")
    parser.add_argument("--max-wait", type=float, default=600.0, help="seconds to wait for the import (default 600)")
    parser.add_argument("--validate", action="store_true", help="run the UxmlCodegenCli validate verb on the result folder")
    parser.add_argument("--prefix", help="with --validate: expected USS class prefix")
    parser.add_argument("--json", action="store_true", help="with --validate: machine-readable validator output")
    parser.add_argument("--cli", nargs="+", metavar="ARG",
                        help="with --validate: validator command prefix instead of 'dotnet run --project Tools~/UxmlCodegenCli --', "
                             "such as: dotnet /path/UxmlCodegenCli.dll (put it last: it takes every following argument)")
    parser.add_argument("--save-result", metavar="PATH",
                        help="where to save the final import result as JSON (default: "
                             "<project>/Library/CuvaraFigma/last-import-<file>.json, read by Window > Cuvara > Figma Diagnostics)")
    parser.add_argument("--no-save-result", action="store_true", help="do not save the import result")
    parser.add_argument("--dry-run", action="store_true", help="print the requests without sending them (saves nothing)")
    return parser.parse_args(argv)


def keep_result(args, project_root, reply, waited=None):
    """Saves the final reply's import result unless --no-save-result."""
    if args.no_save_result:
        return
    document = result_document(reply, args.url, waited)
    save_result(args.save_result or default_result_path(project_root, document), document)


def main(argv):
    args = parse_args(argv)
    if "figma.com/" not in args.url or "node-id=" not in args.url:
        print("error: the link must be a Figma frame link with node-id (in Figma: Copy link to selection)", file=sys.stderr)
        return 2

    parameters = {"url": args.url, "headless": not args.no_headless}
    if args.output_folder:
        parameters["output_folder"] = args.output_folder

    try:
        if args.dry_run:
            pipeline = Pipeline(port="<port>", token=None, dry_run=True)
            pipeline.exec("figma_import_url", parameters, QUEUE_TIMEOUT_MS, 0)
            pipeline.exec("figma_import_status", {"request_id": "<requestId from the reply>"}, STATUS_TIMEOUT_MS, 0)
            print("(repeat figma_import_status after retryAfterSeconds until state is done, failed or expired)")
            return 0

        project_root = find_project_root(args.project)
        if project_root is None:
            raise HelperError(f"no {PORT_FILE} above {os.path.abspath(args.project)}: "
                              "open the project in Unity with com.unity.ui.figma installed")
        port, token = read_port_file(project_root)
        pipeline = Pipeline(port, token, dry_run=False)
        deadline = time.monotonic() + args.max_wait

        reply = pipeline.exec("figma_import_url", parameters, QUEUE_TIMEOUT_MS, deadline)
        while True:
            request_id = reply.get("requestId")
            state = reply.get("state")
            if reply.get("ok") is False and state is None:
                # Refused before queueing (bad link, too many requests, unknown request).
                print(json.dumps(reply, indent=2))
                keep_result(args, project_root, reply)
                return 1
            if state in FINISHED_STATES:
                break
            if not request_id:
                raise HelperError(f"unexpected reply: {json.dumps(reply)[:500]}")
            if time.monotonic() >= deadline:
                print(json.dumps(reply, indent=2))
                print(f"error: still '{state}' after {args.max_wait:.0f} s", file=sys.stderr)
                # Saved as unfinished, so the diagnostics window shows the attempt; the request may still finish.
                keep_result(args, project_root, reply, waited=args.max_wait)
                return 1
            wait = reply.get("retryAfterSeconds") or 3
            note = reply.get("waitingFor")
            print(f"{request_id}: {state}{' (waiting for ' + note + ')' if note else ''}; next poll in {wait} s",
                  file=sys.stderr)
            time.sleep(float(wait))
            reply = pipeline.exec("figma_import_status", {"request_id": request_id}, STATUS_TIMEOUT_MS, deadline)

        print(json.dumps(reply, indent=2), flush=True)
        keep_result(args, project_root, reply)
        result = reply.get("result") if isinstance(reply.get("result"), dict) else {}
        if reply.get("state") != "done" or not result.get("ok"):
            return 1

        if args.validate:
            folder = result.get("folder")
            if not folder:
                raise HelperError("the import result names no folder to validate")
            code = run_validator(args, project_root, folder)
            if code != 0:
                return code
        return 0
    except HelperError as error:
        print(f"error: {error}", file=sys.stderr)
        return 2
    except KeyboardInterrupt:
        return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
