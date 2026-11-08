# Moving the common parts of penpot-uitoolkit-bridge into this package

Status: **phase A done on branch `feat/uitoolkit/penpot-common`** (copy into this package, 2026-11-08). Not released: no version
bump, no tag, and the client still pins this package at its current version. Phase B (the bridge depends on this package and
drops its copies) is later work in the bridge repository.

The plan is `docs/MOVE-TO-UITOOLKIT.md` in `Cuvara/penpot-uitoolkit-bridge`. This page is its step 1 (inventory) and records
step 3 (copy) for the parts that are generic and not changing. The owner asked on 2026-11-08 to do this before the plan's
prerequisites 1 and 2 (N production screens, X weeks of a stable API) hold. Source: bridge `origin/main` at `068bd09`.

## What moved, what moves later, what stays

| Bridge part (package `com.cuvara.penpot-bridge`, folder `unity/`) | Verdict | Here |
|---|---|---|
| Motion helpers, `Runtime/Motion/` (inside assembly `Cuvara.PenpotBridge.Runtime`) | **moved now** | `Runtime/Motion/`, own assembly `Cuvara.UIToolkit.Motion`, no references. [MOTION.md](MOTION.md) |
| Motion data and timeline tests, `Tests/Motion/` (92 cases) | **moved now** | `Tests/Runtime/Motion/`, `Cuvara.UIToolkit.Motion.Tests` (EditMode, 92 cases) |
| `PenpotArtImporter`, `PenpotBridgeSettings`, `SliceSidecar` (Editor) | **moved now**, importer opt-in | `Editor/SlicedArt/` in `Cuvara.UIToolkit.Editor`. [SLICED-ART.md](SLICED-ART.md) |
| `PenpotImportChecker`, `CheckResult`, internal `PenpotPaths`, `PenpotJson` (Editor) | **moved now** | `Editor/ImportCheck/` in `Cuvara.UIToolkit.Editor` |
| `PenpotArtImporterTests`, `SliceSidecarContractTests`, `PenpotImportCheckerTests`, `TempFolder` (16 cases) | **moved now** (+1 case) | `Tests/Editor/SlicedArt/`, `Tests/Editor/ImportCheck/` (17 cases) |
| `PenpotPrototypeRunner`, `PenpotInputSimulator`, `PenpotClicks`, `PenpotFlow` types, `PenpotTransition`, `PenpotMotionSheets`, `PenpotTransitionSignal` | **later** | after the bridge's in-flight `fix/runner/perf` and `feat/motion/crossfade` merge; `flow.json` needs a version and a schema first (plan step 2) |
| PlayMode tests: `PenpotMotionTests` (28, needs `PenpotInputSimulator`), runner, bindings, scroll, transition, ownership, gate 7/8 tests | **later** | with the runner |
| Bindings convention (`<Screen>Bindings`) | **stays** as a convention | this package already has `Require<T>` and its own UXML codegen; the bridge generator stays in the bridge |
| `PenpotScreenCapture`, `CaptureOptions`, `PenpotLayoutDump`, `PenpotPreview`, `PenpotScreenPreview`, `PenpotPrototypeEditorHost` | **stays** | they call internal Unity APIs by reflection (checked against 6000.3.3); a package with a support promise should not carry that |
| `PenpotBridgeMenu`, `PenpotEditorState`, `PenpotTestRun` | **stays** | bridge tooling (menu `Tools/Penpot Bridge`, Editor state JSON for the bridge driver, test-run recorder) |
| Python/JS tool chain, IR schema, converter, lint, gates, reports, Penpot conventions | **stays** | build tool, not a Unity package |

## API mapping (old name -> new name)

### Motion (runtime)

| penpot-uitoolkit-bridge | com.cuvara.uitoolkit |
|---|---|
| namespace `Cuvara.PenpotBridge.Runtime.Motion` | namespace `Cuvara.UIToolkit.Motion` |
| assembly `Cuvara.PenpotBridge.Runtime` (motion files inside it) | assembly `Cuvara.UIToolkit.Motion` (`Runtime/Motion/Cuvara.UIToolkit.Motion.asmdef`) |
| test assembly `Cuvara.PenpotBridge.Motion.Tests` | `Cuvara.UIToolkit.Motion.Tests` |
| `InternalsVisibleTo` `Cuvara.PenpotBridge.Motion.Tests`, `Cuvara.PenpotBridge.PlayMode.Tests` | `InternalsVisibleTo` `Cuvara.UIToolkit.Motion.Tests` only |
| `[CreateAssetMenu]` `Penpot Bridge/Motion Presets` | `Cuvara/UI Toolkit/Motion Presets` |
| fixture path `Packages/com.cuvara.penpot-bridge/Tests/Motion/Fixtures/ExamplePresets.json` | `Packages/com.cuvara.uitoolkit/Tests/Runtime/Motion/Fixtures/ExamplePresets.json` |

Type and member names are unchanged (none contained "Penpot"): `UiMotion`, `MotionClip`, `MotionHandle` (+ `MotionAwaiter`),
`MotionTiming`, `MotionEasing` (+ `Kind`), `MotionValues`, `MotionOptions`, `MotionChannels`, `MotionResult`, `MotionCancel`,
`MotionMode`, `MotionLoop`, `MotionColor`, `IMotionTarget`, `IMotionDriver`, `VisualElementTarget`, `MotionExamples`,
`MotionPresets`, `MotionPresetSet`, `MotionNamedDuration`, `MotionPresetEntry`, `MotionStateEntry`, `MotionPreset`,
`MotionPresetAsset`. Behaviour is identical: the `.cs` files differ from the bridge only in the namespace line, the
`InternalsVisibleTo` list, the menu path and doc comments that pointed at bridge documents.

### Sliced art and import checker (Editor)

| penpot-uitoolkit-bridge (`Cuvara.PenpotBridge.Editor`) | com.cuvara.uitoolkit | Change |
|---|---|---|
| `PenpotArtImporter` | `Cuvara.UIToolkit.Editor.SlicedArt.SlicedArtImporter` | scope check also requires `SlicedArtSettings.Enabled`; `GetVersion()` stays 2 |
| `PenpotArtImporter.Apply(TextureImporter, SliceSidecar)` | `SlicedArtImporter.Apply` | same |
| `PenpotBridgeSettings` | `SlicedArtSettings` | **new switch** `Enabled` (default false) and `SetEnabled(bool)`; settings file `ProjectSettings/PenpotBridgeSettings.json` `{"roots":[...]}` -> `ProjectSettings/CuvaraSlicedArtSettings.json` `{"enabled":true,"roots":[...]}`; **no default root** (the bridge defaults to `Assets/UI/PenpotTest/`) |
| `PenpotBridgeSettings.DefaultRoot` | removed | a project-specific folder has no place in the package |
| `PenpotBridgeSettings.Roots`, `SetRoots`, `Reload`, `IsInScope`, `ProjectRoot`, `SettingsFile` | `SlicedArtSettings.*` | `IsInScope` is false while disabled; `Reload` also resets the switch |
| `SliceSidecar` (`Suffix`, `version`, `border`, `pixelsPerUnit`, `maxSize`, `group`, `SidecarPathFor`, `PngPathFor`, `TryLoad`, `ToSpriteBorder`) | `Cuvara.UIToolkit.Editor.SlicedArt.SliceSidecar` | same; file format unchanged (`version` 1) |
| `PenpotImportChecker` (`Check`, `CheckToJson`) | `Cuvara.UIToolkit.Editor.ImportCheck.UiImportChecker` | same; one error text now says "is a sprite importer configured for this file?" |
| `CheckResult` (`Errors`, `Warnings`, `Logs`, `Checked`, `Pass`, `ToJson`) | `UiImportCheckResult` | same JSON keys |
| internal `PenpotPaths.Resolve` | internal `AssetReferencePaths.Resolve` | same |
| internal `PenpotJson` | internal `UiImportJson` | same |
| log prefix `[PenpotBridge]` | `[Cuvara.UIToolkit]` | |

## Two importers must not run in one project

The client installs the bridge package today, and the bridge importer is active under `Assets/UI/PenpotTest/`. If this package
also ran its importer on the same files, the two postprocessors would both write the texture settings and the last one would
win. So `SlicedArtImporter` is off unless `ProjectSettings/CuvaraSlicedArtSettings.json` sets `"enabled": true`.

Phase B order in the bridge: depend on the released version of this package, delete `PenpotArtImporter` /
`PenpotBridgeSettings` / `SliceSidecar`, and in the same change write `ProjectSettings/CuvaraSlicedArtSettings.json` with the
roots the bridge used (`Assets/UI/PenpotTest/` or the project's `PenpotBridgeSettings.json` roots). Never enable this importer
while the bridge still ships its own.

The import checker has no such conflict: it is on demand. The bridge's Python driver calls
`Cuvara.PenpotBridge.Editor.PenpotImportChecker.CheckToJson` (`bridge/unity_client.py`); phase B changes that call to
`Cuvara.UIToolkit.Editor.ImportCheck.UiImportChecker.CheckToJson`. `bridge/motion.py` names
`Cuvara.PenpotBridge.Runtime.Motion.MotionPresetSet` in a docstring only.

## What the client uses today

Checked in `IndieRPGMMOAdventure` on 2026-11-08: no C# under `Assets/` references a `Cuvara.PenpotBridge` type, motion type or
checker; the client uses the bridge importer through its default root `Assets/UI/PenpotTest/` (no
`ProjectSettings/PenpotBridgeSettings.json`) and the checker through the bridge's sync gates. The client pins
`com.cuvara.uitoolkit` at `v0.7.2`, so none of this reaches it until a release and a pin bump (plan prerequisite 6 and step 4).

## Open items

- `feat/motion/crossfade` (bridge, open) adds a `hover-fade` preset to `Tests/Motion/Fixtures/ExamplePresets.json` and four
  asserts to `MotionDataTests`; runtime motion code is unchanged by it. Re-sync those two files here after it merges.
- Runner move (plan step 2 first: rename behind `[Obsolete]` aliases, version `flow.json`, add its schema), then the
  PlayMode motion tests with it.
- Release: minor version, CHANGELOG section, client pin bump through the `pin-bump` workflow, then phase B in the bridge.
