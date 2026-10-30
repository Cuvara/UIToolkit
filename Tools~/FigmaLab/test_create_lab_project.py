#!/usr/bin/env python3
"""Self-test for create-lab-project.py. Creates lab projects in a temp folder only; never runs Unity.

Run:  python3 Tools~/FigmaLab/test_create_lab_project.py      (Python 3.8+, standard library only)
"""

import importlib.util
import json
import os
import re
import shutil
import sys
import tempfile
import unittest

sys.dont_write_bytecode = True  # never leave __pycache__ in the package
HERE = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location("create_lab_project", os.path.join(HERE, "create-lab-project.py"))
lab = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(lab)
CHECKOUT = os.path.normpath(os.path.join(HERE, os.pardir, os.pardir))


class LabProjectTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.mkdtemp(prefix="cuvara-figma-lab-test-")
        self.target = os.path.join(self.temp, "lab")

    def tearDown(self):
        shutil.rmtree(self.temp, ignore_errors=True)

    def manifest(self):
        with open(os.path.join(self.target, "Packages", "manifest.json"), encoding="utf-8") as handle:
            return json.load(handle)

    def test_manifest_references_the_checkout_the_connector_and_the_ci_setup(self):
        lab.create(self.target, CHECKOUT, "6000.3.9f1", True, False)
        manifest = self.manifest()
        deps = manifest["dependencies"]

        self.assertEqual(deps["com.cuvara.uitoolkit"], "file:" + os.path.abspath(CHECKOUT).replace("\\", "/"))
        self.assertEqual(deps["com.unity.ui.figma"], "0.3.0-exp.1")
        self.assertEqual(deps["com.unity.entities"], "1.4.8")
        with open(os.path.join(CHECKOUT, "package.json"), encoding="utf-8") as handle:
            declared = json.load(handle)["dependencies"]
        self.assertEqual(deps["com.cysharp.unitask"], declared["com.cysharp.unitask"])
        self.assertEqual(deps["jp.hadashikick.vcontainer"], declared["jp.hadashikick.vcontainer"])
        self.assertEqual(manifest["scopedRegistries"],
                         [{"name": "OpenUPM", "url": "https://package.openupm.com", "scopes": ["com.cysharp", "jp.hadashikick"]}])
        self.assertEqual(manifest["testables"], ["com.cuvara.uitoolkit"])

        with open(os.path.join(self.target, "ProjectSettings", "ProjectVersion.txt"), encoding="utf-8") as handle:
            self.assertEqual(handle.read(), "m_EditorVersion: 6000.3.9f1\n")
        self.assertTrue(os.path.isfile(os.path.join(self.target, "Assets", "Editor", "CuvaraFigmaLab", "CuvaraFigmaLabSetup.cs")))
        self.assertTrue(os.path.isdir(os.path.join(self.target, "Assets", "Figma")))

    def test_pins_match_the_ci_workflow(self):
        with open(os.path.join(CHECKOUT, ".github", "workflows", "ci.yml"), encoding="utf-8") as handle:
            ci = handle.read()
        for name, version in list(lab.CI_DEPENDENCIES.items()) + [lab.ENTITIES, lab.CONNECTOR]:
            self.assertRegex(ci, r'"%s":\s*"%s"' % (re.escape(name), re.escape(version)), name)
        self.assertIn('"url": "https://package.openupm.com"', ci)

    def test_no_entities_leaves_entities_out(self):
        lab.create(self.target, CHECKOUT, "6000.3.9f1", False, False)
        self.assertNotIn("com.unity.entities", self.manifest()["dependencies"])

    def test_refuses_a_non_empty_folder_without_force_and_keeps_foreign_files_with_it(self):
        os.makedirs(self.target)
        foreign = os.path.join(self.target, "keep.txt")
        with open(foreign, "w") as handle:
            handle.write("mine")
        with self.assertRaises(lab.LabError):
            lab.create(self.target, CHECKOUT, "6000.3.9f1", True, False)
        lab.create(self.target, CHECKOUT, "6000.3.9f1", True, True)
        with open(foreign) as handle:
            self.assertEqual(handle.read(), "mine")

    def test_refuses_a_folder_inside_the_checkout_and_a_foreign_package(self):
        with self.assertRaises(lab.LabError):
            lab.create(os.path.join(CHECKOUT, "Lab"), CHECKOUT, "6000.3.9f1", True, False)
        other = os.path.join(self.temp, "other")
        os.makedirs(other)
        with open(os.path.join(other, "package.json"), "w") as handle:
            json.dump({"name": "com.example.other"}, handle)
        with self.assertRaises(lab.LabError):
            lab.create(self.target, other, "6000.3.9f1", True, False)
        self.assertFalse(os.path.exists(os.path.join(CHECKOUT, "Lab")))


if __name__ == "__main__":
    unittest.main(verbosity=2)
