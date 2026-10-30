#!/usr/bin/env python3
"""Create a throwaway Unity project for a real Figma import test of com.cuvara.uitoolkit.

The project references this package through a file: path, plus Unity's UI Toolkit Connector for
Figma (com.unity.ui.figma 0.3.0-exp.1, which brings com.unity.pipeline) and the package's OpenUPM
dependencies, with the same dependencies and scoped registry the CI test job bootstraps
(.github/workflows/ci.yml, jobs "test" and "figma-connector"). It also copies a small Editor
script that, on load, turns on Pipeline's "Allow Browser Clients" only when the documented
settings file already has that field, and otherwise prints the manual step.

This script never starts Unity and never touches any other project. It refuses a non-empty target
folder unless --force is given, and even then only writes its own files (it deletes nothing).

Usage:
  python3 Tools~/FigmaLab/create-lab-project.py [path] [--package <checkout>] [--unity 6000.3.9f1]
                                                [--no-entities] [--force]

Then follow Documentation~/FIGMA-REAL-IMPORT.md. Python 3.8+, standard library only.
"""

import argparse
import json
import os
import shutil
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_PATH = "C:/tmp/uitk-figma-lab" if os.name == "nt" else "/tmp/uitk-figma-lab"
DEFAULT_PACKAGE = os.path.normpath(os.path.join(HERE, os.pardir, os.pardir))
DEFAULT_UNITY = "6000.3.9f1"

# Pins shared with .github/workflows/ci.yml (jobs "test" and "figma-connector"). Keep them equal.
CONNECTOR = ("com.unity.ui.figma", "0.3.0-exp.1")
CI_DEPENDENCIES = {
    "com.unity.test-framework": "1.6.0",
    "com.unity.inputsystem": "1.18.0",
}
ENTITIES = ("com.unity.entities", "1.4.8")
OPENUPM = {"name": "OpenUPM", "url": "https://package.openupm.com", "scopes": ["com.cysharp", "jp.hadashikick"]}
OPENUPM_PACKAGES = ("com.cysharp.unitask", "jp.hadashikick.vcontainer")

PROJECT_SETTINGS = """%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!129 &1
PlayerSettings:
  companyName: Cuvara
  productName: UIToolkitFigmaLab
  activeInputHandler: 1
"""


class LabError(Exception):
    pass


def read_package(checkout):
    path = os.path.join(checkout, "package.json")
    try:
        with open(path, encoding="utf-8") as handle:
            package = json.load(handle)
    except (OSError, ValueError) as error:
        raise LabError(f"cannot read {path}: {error}")
    if package.get("name") != "com.cuvara.uitoolkit":
        raise LabError(f"{path} is not com.cuvara.uitoolkit (name: {package.get('name')!r})")
    return package


def build_manifest(checkout, package, with_entities):
    """The lab's Packages/manifest.json as a dict."""
    dependencies = {"com.cuvara.uitoolkit": "file:" + os.path.abspath(checkout).replace("\\", "/")}
    dependencies.update(CI_DEPENDENCIES)
    if with_entities:
        dependencies[ENTITIES[0]] = ENTITIES[1]
    dependencies[CONNECTOR[0]] = CONNECTOR[1]
    # Explicit, at the versions the package itself declares, so the lab resolves exactly those.
    declared = package.get("dependencies") or {}
    for name in OPENUPM_PACKAGES:
        if name not in declared:
            raise LabError(f"package.json no longer declares {name}; update create-lab-project.py")
        dependencies[name] = declared[name]
    return {
        "dependencies": dependencies,
        "scopedRegistries": [dict(OPENUPM)],
        "testables": ["com.cuvara.uitoolkit"],
    }


def write(path, text, written):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(text)
    written.append(path)


def create(target, checkout, unity, with_entities, force):
    """Writes the lab project; returns the files written."""
    target = os.path.abspath(target)
    checkout = os.path.abspath(checkout)
    package = read_package(checkout)

    if os.path.isfile(target):
        raise LabError(f"{target} is a file")
    if os.path.isdir(target) and os.listdir(target) and not force:
        raise LabError(f"{target} is not empty; choose another path or pass --force (it overwrites only the lab's own files)")
    if os.path.commonpath([target, checkout]) == checkout:
        raise LabError("the lab project must be outside the package checkout")

    written = []
    manifest = build_manifest(checkout, package, with_entities)
    write(os.path.join(target, "Packages", "manifest.json"), json.dumps(manifest, indent=2) + "\n", written)
    write(os.path.join(target, "ProjectSettings", "ProjectVersion.txt"), f"m_EditorVersion: {unity}\n", written)
    write(os.path.join(target, "ProjectSettings", "ProjectSettings.asset"), PROJECT_SETTINGS, written)

    template = os.path.join(HERE, "template")
    for root, _, files in os.walk(template):
        for name in files:
            source = os.path.join(root, name)
            destination = os.path.join(target, os.path.relpath(source, template))
            os.makedirs(os.path.dirname(destination), exist_ok=True)
            shutil.copyfile(source, destination)
            written.append(destination)

    os.makedirs(os.path.join(target, "Assets", "Figma"), exist_ok=True)
    return written


def parse_args(argv):
    parser = argparse.ArgumentParser(description="Create a throwaway Unity project for a real Figma connector import test.")
    parser.add_argument("path", nargs="?", default=DEFAULT_PATH, help=f"project folder to create (default {DEFAULT_PATH})")
    parser.add_argument("--package", default=DEFAULT_PACKAGE, help="com.cuvara.uitoolkit checkout to reference (default: this one)")
    parser.add_argument("--unity", default=DEFAULT_UNITY, help=f"Unity version for ProjectVersion.txt (default {DEFAULT_UNITY})")
    parser.add_argument("--no-entities", action="store_true", help="leave com.unity.entities out (CI's test job has it)")
    parser.add_argument("--force", action="store_true", help="write into a non-empty folder (overwrites only the lab's own files)")
    return parser.parse_args(argv)


def main(argv):
    args = parse_args(argv)
    try:
        written = create(args.path, args.package, args.unity, not args.no_entities, args.force)
    except LabError as error:
        print(f"error: {error}", file=sys.stderr)
        return 2
    for path in written:
        print("wrote " + path)
    print(f"\nLab project ready at {os.path.abspath(args.path)}. Open it with Unity {args.unity} "
          "(Unity Hub > Add > Add project from disk), then follow Documentation~/FIGMA-REAL-IMPORT.md.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
