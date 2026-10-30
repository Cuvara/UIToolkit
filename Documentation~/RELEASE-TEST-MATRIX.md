# Release test matrix

This page lists every test suite in the package, where each one runs, and what a release must
show before it is tagged. Use it as the checklist for a release candidate and as the map when
a CI job fails.

Counts are test cases (one per `[Test]`, `[UnityTest]` or `[TestCase]` row). They were counted
on branch `feat/uitoolkit/figma-qa` (2026-10-30). The dotnet harness counts were executed. The
Unity counts are static, because CI runs them. If you add or remove tests, update this table in
the same change.

## 1. Where each suite runs

| Assembly / tool | Folder | Unity mode | CI job(s) | Connector needed | Cases |
|---|---|---|---|---|---|
| `Cuvara.UIToolkit.Editor.Tests` | `Tests/Editor/` (+ `FigmaCore/`) | EditMode | `test`, `figma-connector` | no | 270 |
| `Cuvara.UIToolkit.Editor.Figma.Tests` | `Tests/Editor/Figma/` | EditMode | `figma-connector` only | yes (gated) | 124 |
| `Cuvara.UIToolkit.Tests` | `Tests/Runtime/` (root, `Collections/`, `TestSupport/`, `Theme/`, `ViewModel/`) | PlayMode | `test` | no | 207 |
| `Cuvara.UIToolkit.Flow.Tests` | `Tests/Runtime/Flow/` | PlayMode | `test` | no | 109 |
| `Cuvara.UIToolkit.Ecs.Tests` | `Tests/Runtime/Ecs/` | PlayMode | `test` (entities is in its manifest) | no (gated on `com.unity.entities`) | 20 |
| `UxmlCodegenCli` drift check | `Tools~/UxmlCodegenCli/` | none (.NET 10) | client repo codegen-drift check | no | n/a |
| `UxmlCodegenCli validate` on fixtures | `Tools~/UxmlCodegenCli/Fixtures/figma/` | none (.NET 10) | `validate` | no | 2 runs |
| `cli_smoke.py` (CLI exit codes and JSON shape) | `Tools~/UxmlCodegenCli/cli_smoke.py` | none (.NET 10 + Python 3) | `validate` | no | 30 |
| `test_figma_import.py` (headless helper against a mock Pipeline server) | `Tools~/FigmaImport/` | none (Python 3) | `validate` | no | 12 |
| `test_create_lab_project.py` (real-import lab generator) | `Tools~/FigmaLab/` | none (Python 3) | `validate` | no | 5 |
| Plain-C# NUnit harness (Unity-free sources) | `Editor/FigmaCore/`, `Editor/Codegen/Core/`, Unity-free `Editor/Figma/` and `Editor/ScreenCreator/` types | none (net8) | local only | no | 374 |

Notes:

- `test` runs `testMode: all`, so it runs both EditMode and PlayMode. `figma-connector` runs
  `testMode: EditMode` only. It repeats the ungated editor suite and adds the gated one; it does
  not run the runtime suites.
- `Cuvara.UIToolkit.Tests` and `Cuvara.UIToolkit.Flow.Tests` have no platform restriction, so the
  Test Runner lists them under PlayMode. Code inside `#if UNITY_EDITOR` in those files (for
  example the theme default checks in `CuvaraThemeTests`) runs because PlayMode tests run in the
  Editor.
- Both Unity jobs fail on zero executed tests. `figma-connector` also fails when no
  `Cuvara.UIToolkit.Editor.Figma.Tests` case ran, which catches a define that did not take effect.
- The `samples` job compiles the imported samples; it runs no tests. The `install-probe` jobs
  compile the package from the documented install; they run no tests.

## 2. Connector present and absent

| Setup | What compiles | What must pass |
|---|---|---|
| No `com.unity.ui.figma` (`test` job, a game without the connector) | `Cuvara.UIToolkit.Editor` including `Editor/FigmaCore/`; the gated `Editor/Figma/` assemblies are skipped by `defineConstraints` | all ungated suites, including `FigmaIsolationTests` and the `FigmaCore` validator tests |
| `com.unity.ui.figma` 0.3.0-exp.1 (`figma-connector` job) | the above, plus `Cuvara.UIToolkit.Editor.Figma` and its tests | the ungated editor suite again, plus the gated suite (at least one gated case must execute) |
| A connector version outside the `versionDefines` expression | same as "no connector" | same as "no connector"; the `figma-connector` job would fail on its gated-case gate |

`Tests/Editor/FigmaIsolationTests.cs` checks the isolation rules in the ungated assembly. It
runs in both setups:

- no ungated asmdef references `Cuvara.UIToolkit.Editor.Figma` (by name or `GUID:`) or any
  Figma assembly, and none reacts to `com.unity.ui.figma` through `versionDefines`;
- no asmdef references the connector's internal `Unity.UIToolkit.Figma.Editor`;
- `Editor/Figma` and `Tests/Editor/Figma` keep the `com.unity.ui.figma` version define and the
  `CUVARA_UITOOLKIT_FIGMA` define constraint, and stay Editor-only; no other asmdef is gated;
- `Runtime/` asmdefs reference no Figma or editor assembly;
- `Editor/FigmaCore/*.cs` has no `UnityEngine` or `UnityEditor` reference;
- no loaded `Cuvara.UIToolkit*` assembly except the gated ones links the integration or the
  connector.

## 3. Coverage by area

| Area | Suites | What is covered |
|---|---|---|
| Validator rules `FIG001`-`FIG051` | `FigmaImportValidatorTests`, `FigmaImportValidatorQaTests`, CLI fixtures | one test per rule; malformed UXML (unclosed tag, duplicate attribute, undefined entity, DOCTYPE, two roots, empty file, truncated tag); BOM and CRLF in UXML, USS and the manifest (same line numbers as LF); nested templates; missing or empty `src`; duplicate `<Style>`; every reference form (`project://database/Assets/...`, `/Assets/...`, relative, `../`, percent-encoded, `Packages/...`, other schemes, `data:`, `resource()`); thresholds at 0, at the limit and unlimited, per document; deterministic order and JSON |
| USS reader | `FigmaUssTests`, `FigmaUssQaTests` | unterminated comment and rule; stray `}`; nested `{`; braces, semicolons and comment openers inside strings; escaped quotes; strings that end at a newline; `@import` (quoted and `url()`, line numbers, checked as `FIG040`); `url()` quoted, unquoted, with spaces, parentheses and CSS escapes; CRLF lines |
| Tokens | `FigmaImportValidatorTests`, `FigmaImportValidatorQaTests` | aliases through `tokens.uss`; aliases to an undefined token; tokens defined only in a mode sheet or only in the manifest; `--unity-` and `--cuvara-` prefixes; exact, case-sensitive names; tokens in comments do not count |
| Manifest and JSON | `FigmaManifestReaderTests`, `FigmaJsonTests`, `FigmaJsonQaTests`, `FigmaImportValidatorQaTests` | truncated, empty, binary and non-object manifests (`FIG002`, the rest of the folder is still checked); unknown fields; wrong types; empty screens; `generated[]` and `assetName` paths that leave the import folder (`../../x`, rooted); backslash paths; depth cap; escapes and numbers |
| CLI | `cli_smoke.py`, CI `validate` step | exit 0, 1 and 2 for every documented path (no arguments, unknown verb as a drift root, case-sensitive verb, bad or missing option values, missing or empty folder, two folders, `--help`); stable `--json` keys and order; drift clean, drifted, broken and un-enrolled |
| Profile paths and token bridge | `FigmaPathsTests`, `FigmaPathsQaTests`, `FigmaTokenBridgeTests`, `FigmaTokenBridgeQaTests` | bridge folder inside the connector folder; `..` segments in both folders; absolute Windows and Unix paths; backslashes; case differences; trailing slashes; empty, duplicate and invalid roles; absent, invalid, unknown and case-different Figma tokens; output always parses as one `:root` rule |
| Theme runtime | `CuvaraThemeTests`, `CuvaraThemeServiceQaTests` | default sheet; component classes; inline fallback; sheet order base, overrides, mode across several roots and late roots; idempotent mode toggles; a mode that is also an override or the base; a service without a base sheet; nested `OverrideDefaultStyleSheet` |
| Optional connector | `FigmaIsolationTests` | section 2 |

## 4. Run it locally

You do not need Unity for the Unity-free parts.

```bash
# CLI: build once, then the fixtures and the exit-code smoke test (from the package root)
dotnet build "Tools~/UxmlCodegenCli/UxmlCodegenCli.csproj" -c Release -o /tmp/uxmlcli
dotnet /tmp/uxmlcli/UxmlCodegenCli.dll validate Tools~/UxmlCodegenCli/Fixtures/figma/good/Assets/Figma/Shop-Demo --prefix rpg --max-inline 5 --max-absolute 1
python3 "Tools~/UxmlCodegenCli/cli_smoke.py" /tmp/uxmlcli/UxmlCodegenCli.dll
# Python self-tests of the Figma helper and the lab generator (no Unity, no Figma)
python3 "Tools~/FigmaImport/test_figma_import.py"
python3 "Tools~/FigmaLab/test_create_lab_project.py"
```

Building the CLI inside the package writes `Tools~/UxmlCodegenCli/obj/`. The repository has no
`.gitignore`, so delete that folder or build from a copy of the project file.

The plain-C# NUnit harness is a throwaway `net8.0` test project that compiles
`Editor/FigmaCore/*.cs`, `Editor/Codegen/Core/*.cs`, the Unity-free `Editor/Figma` types
(`FigmaPaths`, `FigmaTokenBridge`, `FigmaMappingPreset`), `Editor/ScreenCreator` plan and
templates, and the matching tests (`Tests/Editor/FigmaCore/*.cs`, `Tests/Editor/Figma/*Tests.cs`
except `FigmaMappingPresetSerializedTests`, `Tests/Editor/ScreenCreator*Tests.cs`,
`Tests/Editor/FigmaIsolationTests.cs`) with NUnit 3.14. It does not replace the Unity run.

Unity's bundled NUnit is 3.5. It resolves `Has.Count` by reflection and fails on arrays and
array-backed lists, and the 3.14 harness does not catch that. Write
`Assert.That(x.Count, Is.EqualTo(n))`, or `Has.Length` for arrays.

## 5. Release checklist

A release candidate is ready when every row is green on the release commit:

| # | Check | Evidence |
|---|---|---|
| 1 | `validate` job: package.json, standalone, USS prefix, CHANGELOG version, `.meta` coverage, samples, asmdef names | CI job log |
| 2 | `validate` job: good fixture exit 0 (0 errors, 0 warnings, 3 info), bad fixture exit 1 with exactly 7 errors and 8 warnings | CI job log |
| 3 | `test` job: executed > 0, failed = 0, errors = 0 | `Assert tests ran and passed` line, test-results artifact |
| 4 | `figma-connector` job: executed > 0, gated (`figma=`) > 0, failed = 0 | `::notice::` line |
| 5 | `samples` job compiles | CI job log |
| 6 | `install-probe (documented)` compiles; `bare` is informational | CI job log |
| 7 | `cli_smoke.py` reports `30/30 passed`; `test_figma_import.py` and `test_create_lab_project.py` report `OK` | `validate` job log |
| 8 | Client project codegen drift check (`uitoolkit.codegen-cli/client-codegen-drift`) | Factory run-checks evidence |

Record the counts from rows 3 and 4 in the release notes. A count that drops without a
matching test removal is a finding.

## 6. Known gaps

- Theme runtime tests and `FigmaIsolationTests` are compiled against the Unity 6000.3.9f1
  assemblies locally, but only CI executes them.
- XML error line numbers come from the runtime's `XmlReader`. CI runs them under Unity's Mono, the
  local harness under CoreCLR; a difference there shows up as a line-number failure in
  `FigmaImportValidatorQaTests.MalformedUxml_*`.
- The connector itself (import, write-back, Pipeline commands) is not exercised by any automated
  test. `Tools~/FigmaImport/figma-import.py` needs a running Editor and a Figma token.
