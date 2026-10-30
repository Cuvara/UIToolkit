#!/usr/bin/env python3
"""Exit-code and output-shape contract of UxmlCodegenCli.

Runs the built CLI through every documented path and asserts the exit code
(0 = clean, 1 = findings, 2 = usage or IO problem) plus the stable parts of the
output. It needs nothing but Python 3 and the built CLI:

    dotnet build Tools~/UxmlCodegenCli/UxmlCodegenCli.csproj -c Release -o /tmp/uxmlcli
    python3 Tools~/UxmlCodegenCli/cli_smoke.py /tmp/uxmlcli/UxmlCodegenCli.dll

Run it from the package root. Drift cases are built in a temp folder from the
package's own enrolled UXML (Tests/Runtime/ConfirmPopup.uxml), so the package
tree is never written. Prints one line per case and exits 1 on any failure.
"""

import json
import os
import shutil
import subprocess
import sys
import tempfile

FIXTURES = os.path.join("Tools~", "UxmlCodegenCli", "Fixtures", "figma")
GOOD = os.path.join(FIXTURES, "good", "Assets", "Figma", "Shop-Demo")
BAD = os.path.join(FIXTURES, "bad", "Assets", "Figma", "Shop-Demo")
OPTS = ["--prefix", "rpg", "--max-inline", "5", "--max-absolute", "1"]
ENROLLED_UXML = os.path.join("Tests", "Runtime", "ConfirmPopup.uxml")
ENROLLED_GENERATED = os.path.join("Tests", "Runtime", "Generated", "ConfirmPopup.uxml.g.cs")

failures = []
cases = 0


def run(cli, args):
    command = ["dotnet", cli] + args if cli.endswith(".dll") else [cli] + args
    result = subprocess.run(command, capture_output=True, text=True, encoding="utf-8")
    return result.returncode, result.stdout, result.stderr


def case(name, cli, args, code, stdout_has=(), stderr_has=(), check=None):
    global cases
    cases += 1
    actual, out, err = run(cli, args)
    problems = []
    if actual != code:
        problems.append(f"exit {actual}, expected {code}")
    problems += [f"stdout lacks {s!r}" for s in stdout_has if s not in out]
    problems += [f"stderr lacks {s!r}" for s in stderr_has if s not in err]
    if check is not None and not problems:
        try:
            check(out)
        except AssertionError as error:
            problems.append(f"check failed: {error}")
    if problems:
        failures.append(name)
        print(f"FAIL {name}: {'; '.join(problems)}\n  stdout: {out.strip()[:400]}\n  stderr: {err.strip()[:400]}")
    else:
        print(f"ok   {name} (exit {actual})")


def json_shape(expected_errors, expected_warnings):
    def check(out):
        data = json.loads(out)
        assert list(data.keys()) == ["folder", "errors", "warnings", "infos", "diagnostics"], list(data.keys())
        assert (data["errors"], data["warnings"]) == (expected_errors, expected_warnings), (data["errors"], data["warnings"])
        for item in data["diagnostics"]:
            assert list(item.keys()) == ["code", "severity", "file", "line", "message"], list(item.keys())
            assert item["severity"] in ("error", "warning", "info"), item["severity"]
            assert isinstance(item["line"], int) and item["line"] >= 0, item["line"]
        severities = [d["severity"] for d in data["diagnostics"]]
        assert severities.count("error") == data["errors"] and severities.count("warning") == data["warnings"] \
            and severities.count("info") == data["infos"], "counts disagree with the list"
        keys = [(d["file"], d["line"], d["code"], d["message"]) for d in data["diagnostics"]]
        assert keys == sorted(keys, key=lambda k: (k[0].encode("utf-16-be"), k[1], k[2].encode("utf-16-be"), k[3].encode("utf-16-be"))), \
            "diagnostics are not in the documented order"
    return check


def main():
    if len(sys.argv) != 2:
        print(__doc__)
        return 2
    cli = sys.argv[1]
    if not os.path.isfile(os.path.join("package.json")):
        print("run from the package root")
        return 2

    # validate verb
    case("validate good fixture", cli, ["validate", GOOD] + OPTS, 0, stdout_has=["0 error(s), 0 warning(s), 3 info"])
    case("validate bad fixture", cli, ["validate", BAD] + OPTS, 1, stdout_has=["10 error(s), 8 warning(s), 3 info"])
    case("validate good --json shape", cli, ["validate", GOOD] + OPTS + ["--json"], 0, check=json_shape(0, 0))
    case("validate bad --json shape", cli, ["validate", BAD] + OPTS + ["--json"], 1, check=json_shape(10, 8))
    case("validate --json is deterministic", cli, ["validate", BAD] + OPTS + ["--json"], 1,
         check=lambda out: _assert(out == run(cli, ["validate", BAD] + OPTS + ["--json"])[1], "two runs differ"))
    case("validate options before folder", cli, ["validate"] + OPTS + [GOOD], 0)
    case("validate --prefix with trailing dash", cli, ["validate", GOOD, "--prefix", "rpg-"], 0)
    case("validate --no-token-colors drops FIG021", cli, ["validate", BAD, "--no-token-colors", "--json"], 1,
         check=lambda out: _assert(all(d["code"] not in ("FIG020", "FIG021") for d in json.loads(out)["diagnostics"]), "FIG020/021 present"))
    case("validate --help", cli, ["validate", "--help"], 0, stdout_has=["usage: UxmlCodegenCli validate"])
    case("validate -h", cli, ["validate", "-h"], 0, stdout_has=["usage:"])
    case("validate without folder", cli, ["validate"], 2, stderr_has=["missing <importFolder>", "usage:"])
    case("validate missing folder", cli, ["validate", "does-not-exist"], 2, stderr_has=["does not exist"])
    case("validate empty folder argument", cli, ["validate", ""], 2, stderr_has=["does not exist"])
    case("validate two folders", cli, ["validate", GOOD, BAD], 2, stderr_has=["only one import folder"])
    case("validate unknown option", cli, ["validate", GOOD, "--bogus"], 2, stderr_has=["unknown option '--bogus'"])
    case("validate --prefix without value", cli, ["validate", GOOD, "--prefix"], 2, stderr_has=["--prefix needs a value"])
    case("validate --max-inline without value", cli, ["validate", GOOD, "--max-inline"], 2, stderr_has=["--max-inline needs"])
    case("validate --max-inline negative", cli, ["validate", GOOD, "--max-inline", "-1"], 2, stderr_has=["non-negative integer"])
    case("validate --max-absolute not a number", cli, ["validate", GOOD, "--max-absolute", "x"], 2, stderr_has=["non-negative integer"])
    case("validate --max-inline 0 is a warning only", cli, ["validate", GOOD, "--max-inline", "0"], 0, stdout_has=["FIG030"])

    work = tempfile.mkdtemp(prefix="uxmlcli-smoke-")
    try:
        empty_import = os.path.join(work, "empty")
        os.makedirs(empty_import)
        case("validate folder without manifest", cli, ["validate", empty_import], 1, stdout_has=["FIG001"])

        # drift check (no verb): anything that is not a verb is a root
        clean = os.path.join(work, "clean")
        drifted = os.path.join(work, "drifted")
        broken = os.path.join(work, "broken")
        for root in (clean, drifted, broken):
            os.makedirs(os.path.join(root, "Generated"))
            shutil.copyfile(ENROLLED_UXML, os.path.join(root, "ConfirmPopup.uxml"))
            shutil.copyfile(ENROLLED_GENERATED, os.path.join(root, "Generated", "ConfirmPopup.uxml.g.cs"))
            # Outside Tests/Runtime there is no asmdef to name the namespace; pin the same one.
            with open(os.path.join(root, ".uxml-namespace"), "w", encoding="utf-8") as f:
                f.write("Cuvara.UIToolkit.Tests\n")
        with open(os.path.join(drifted, "Generated", "ConfirmPopup.uxml.g.cs"), "a", encoding="utf-8") as f:
            f.write("// hand edit\n")
        with open(os.path.join(broken, "ConfirmPopup.uxml"), "w", encoding="utf-8") as f:
            f.write("<ui:UXML xmlns:ui=\"UnityEngine.UIElements\"><ui:Label>")
        unenrolled = os.path.join(work, "unenrolled")
        os.makedirs(unenrolled)
        shutil.copyfile(ENROLLED_UXML, os.path.join(unenrolled, "ConfirmPopup.uxml"))

        case("drift: clean root", cli, [clean], 0, stdout_has=["1 enrolled UXML file(s) checked", "up to date"])
        case("drift: unenrolled UXML is skipped", cli, [unenrolled], 0, stdout_has=["0 enrolled UXML file(s) checked"])
        case("drift: hand-edited generated file", cli, [drifted], 1, stderr_has=["DRIFTED"])
        case("drift: broken UXML", cli, [broken], 1, stderr_has=["FAILED to regenerate"])
        case("drift: several roots, one drifted", cli, [clean, drifted], 1, stdout_has=["2 enrolled UXML file(s) checked"])
        case("drift: missing root", cli, ["does-not-exist"], 2, stderr_has=["root 'does-not-exist' does not exist"])
        case("drift: unknown verb is a missing root", cli, ["frobnicate"], 2, stderr_has=["root 'frobnicate' does not exist"])
        case("drift: verb is case-sensitive", cli, ["VALIDATE", GOOD], 2, stderr_has=["root 'VALIDATE' does not exist"])
        case("drift: package Tests root", cli, ["Tests"], 0, stdout_has=["up to date"])
    finally:
        shutil.rmtree(work, ignore_errors=True)

    print(f"cli smoke: {cases - len(failures)}/{cases} passed")
    return 1 if failures else 0


def _assert(condition, message):
    assert condition, message


if __name__ == "__main__":
    sys.exit(main())
