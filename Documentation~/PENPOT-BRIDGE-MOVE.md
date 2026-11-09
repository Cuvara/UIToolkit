# Moving the common parts of penpot-uitoolkit-bridge into this package

Status: **phase A done** (`feat/uitoolkit/penpot-common`, PR #14, 2026-11-08: motion, sliced art, import checker) and **phase A2
done** (`feat/uitoolkit/prototype-runner`, 2026-11-09: prototype runner, its PlayMode tests, the `flow.json` contract). Both ship in
**0.9.0**. The client still pins this package at its current version until a pin bump. Phase B (the bridge depends on this package
and drops its copies) is later work in the bridge repository.

The plan is `docs/MOVE-TO-UITOOLKIT.md` in `Cuvara/penpot-uitoolkit-bridge`. This page is its step 1 (inventory) and records
step 3 (copy) for the parts that are generic and not changing. The owner asked on 2026-11-08 to do this before the plan's
prerequisites 1 and 2 (N production screens, X weeks of a stable API) hold. Source: bridge `origin/main` at `068bd09` (phase A) and
`3b490b1` (phase A2 and the motion re-sync).

## What moved and what stays

| Bridge part (package `com.cuvara.penpot-bridge`, folder `unity/`) | Verdict | Here |
|---|---|---|
| Motion helpers, `Runtime/Motion/` (inside assembly `Cuvara.PenpotBridge.Runtime`) | **moved now** | `Runtime/Motion/`, own assembly `Cuvara.UIToolkit.Motion`, no references. [MOTION.md](MOTION.md) |
| Motion data and timeline tests, `Tests/Motion/` (92 cases) | **moved now** | `Tests/Runtime/Motion/`, `Cuvara.UIToolkit.Motion.Tests` (EditMode, 92 cases) |
| `PenpotArtImporter`, `PenpotBridgeSettings`, `SliceSidecar` (Editor) | **moved now**, importer opt-in | `Editor/SlicedArt/` in `Cuvara.UIToolkit.Editor`. [SLICED-ART.md](SLICED-ART.md) |
| `PenpotImportChecker`, `CheckResult`, internal `PenpotPaths`, `PenpotJson` (Editor) | **moved now** | `Editor/ImportCheck/` in `Cuvara.UIToolkit.Editor` |
| `PenpotArtImporterTests`, `SliceSidecarContractTests`, `PenpotImportCheckerTests`, `TempFolder` (16 cases) | **moved now** (+1 case) | `Tests/Editor/SlicedArt/`, `Tests/Editor/ImportCheck/` (17 cases) |
| `PenpotPrototypeRunner`, `PenpotInputSimulator`, `PenpotClicks`, `PenpotFlow` types, `PenpotTransition`, `PenpotMotionSheets`, `PenpotTransitionSignal`, `PenpotPointerWarmUp` | **moved in A2** | `Runtime/Prototype/`, own assembly `Cuvara.UIToolkit.Prototype`, no references. [PROTOTYPE-FLOW.md](PROTOTYPE-FLOW.md) |
| `flow.json` format | **moved in A2** as a versioned contract | [PROTOTYPE-FLOW.md](PROTOTYPE-FLOW.md), [prototype-flow.schema.json](prototype-flow.schema.json); the writer (`bridge/flow.py`) stays in the bridge |
| PlayMode tests: `PenpotPrototypeRunnerTests` (17), `PenpotTransitionTests` (17), `PenpotMotionTests` (28), `PenpotOwnershipTests` (4 of 6), `PenpotScrollTests` (3) | **moved in A2** | `Tests/Runtime/Prototype/`, `Cuvara.UIToolkit.Prototype.Tests` (PlayMode, 69 cases + 11 new `flow.json` contract cases) |
| `PenpotGate7Tests`, `PenpotGate8Tests`, the two gate-7 expectation cases of `PenpotOwnershipTests`, `PenpotPreviewSceneTests` | **stays** | bridge quality gates; they need `PenpotPrototypeEditorHost`, the client's screens or its preview scene |
| `PenpotBindingsTests`, `Generated/ClashBindings.g.cs` | **stays** | they test the bridge's bindings generator; `Generated/MenuBindings.g.cs` is copied as a fixture of the transition-signal test |
| Bindings convention (`<Screen>Bindings`) | **stays** as a convention | this package already has `Require<T>` and its own UXML codegen; the bridge generator stays in the bridge |
| `PenpotScreenCapture`, `CaptureOptions`, `PenpotLayoutDump`, `PenpotPreview`, `PenpotScreenPreview`, `PenpotPrototypeEditorHost` (capture, layout dump, preview) | **stays** | they call internal Unity APIs by reflection (checked against 6000.3.3); a package with a support promise should not carry that |
| `PenpotBridgeMenu`, `PenpotEditorState`, `PenpotTestRun` | **stays** | bridge tooling (menu `Tools/Penpot Bridge`, Editor state JSON for the bridge driver, test-run recorder) |
| Python/JS tool chain (extractor, IR schema, converter including the `flow.json` writer and the bindings generator, lint, `sync`, gates 1-8, reports), Penpot conventions | **stays** | build tool, not a Unity package |

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

Motion re-sync (A2): bridge main `3b490b1` changed no motion runtime file after `068bd09`. Applied here:
`Tests/Runtime/Motion/Fixtures/ExamplePresets.json` gains the `hover-fade` preset (crossfade, `color`, normal, ease-out) and
`MotionDataTests.GeneratedPresets_TheBridgeOutputResolvesWithoutErrors` its four `hover-fade` asserts, both copied byte for byte apart from the
namespace and fixture path. `AssemblyInfo.cs` also grants `InternalsVisibleTo` to `Cuvara.UIToolkit.Prototype.Tests` (the bridge's
equivalent is `Cuvara.PenpotBridge.PlayMode.Tests`). With that, every motion `.cs` and the fixture differ from the bridge only by
the namespace line, the `InternalsVisibleTo` names, the menu path and doc comments that pointed at bridge documents.

### Prototype runner (runtime, A2)

| penpot-uitoolkit-bridge (`Cuvara.PenpotBridge.Runtime`) | com.cuvara.uitoolkit (`Cuvara.UIToolkit.Prototype`) |
|---|---|
| namespace `Cuvara.PenpotBridge.Runtime`, assembly `Cuvara.PenpotBridge.Runtime` | namespace and assembly `Cuvara.UIToolkit.Prototype` (`Runtime/Prototype/Cuvara.UIToolkit.Prototype.asmdef`, no references) |
| `PenpotPrototypeRunner` | `PrototypeRunner` |
| `PenpotInputSimulator` | `UiInputSimulator` |
| `PenpotClicks` | `UiClicks` |
| `PenpotFlowFile` | `PrototypeFlowFile` (+ `const int SupportedVersion = 1`) |
| `PenpotFlowDef`, `PenpotInteraction`, `PenpotAnimation`, `PenpotControl`, `PenpotList`, `PenpotScroll`, `PenpotMotion` | `PrototypeFlowDef`, `PrototypeInteraction`, `PrototypeAnimation`, `PrototypeControl`, `PrototypeList`, `PrototypeScroll`, `PrototypeMotion` |
| `PenpotScreenEntry`, `PenpotTransitionInfo` | `PrototypeScreenEntry`, `PrototypeTransitionInfo` |
| `PenpotTransitionSignal` | `ScreenTransitionSignal` |
| `PenpotMotionSheets` | `MotionSheets` (namespace `Cuvara.UIToolkit.Prototype`, not `.Motion`) |
| internal `PenpotTransition`, `PenpotTrack`, `PenpotPointerWarmUp` | internal `PrototypeTransition`, `PrototypeTrack`, `PointerWarmUp` |
| element names `PenpotHost`, `PenpotOverlays`, `PenpotOverlay-<name>`, `PenpotInputBlock` | `PrototypeHost`, `PrototypeOverlays`, `PrototypeOverlay-<name>`, `PrototypeInputBlock` |
| `PenpotRunnerPanelSettings (created)`, render texture `PenpotRunnerTarget` | `PrototypeRunnerPanelSettings (created)`, `PrototypeRunnerTarget` |
| log prefix `[PenpotBridge]` | `[Cuvara.UIToolkit]` |
| flow version error `Unsupported or empty flow file (version N)` | `... (version N, this runner reads version 1)`; accepted versions unchanged (only 1) |
| test assembly `Cuvara.PenpotBridge.PlayMode.Tests` | `Cuvara.UIToolkit.Prototype.Tests`; fixtures at `Packages/com.cuvara.uitoolkit/Tests/Runtime/Prototype/Fixtures/` |

Member names, constants (`SelectedClass` = `selected`, `SelectedElementName` = `Selected`, `SelectionMarkClass` =
`selection-mark`, `MotionSheets.Suffix` = `.motion`) and behaviour are unchanged: apart from the names above, the `.cs` files differ
from the bridge only in doc comments and the version error text. The tests differ in names and in three places: the theme comes
from a fixture `.tss` that imports Unity's default runtime theme (the bridge found `UnityDefaultRuntimeTheme` in the client), the
scroll probe of gate 7 is a test helper (`ScrollProbe`), and the two gate-7 expectation cases of the ownership tests stayed.

The `.meta` GUIDs are new, as in phase A: both packages are installed in the client until phase B, and duplicate GUIDs would
collide. A scene or prefab that references `PenpotPrototypeRunner` by script GUID must be migrated in Unity in phase B, never by
editing YAML (bridge plan, Risks).

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

- Release: 0.9.0 is prepared (version, CHANGELOG); the tag, the client pin bump through the `pin-bump` workflow and phase B in
  the bridge follow.
- Phase B for the runner: the bridge's `unity/` depends on this package, deletes its runner copy, and its gates, previews,
  `PenpotPrototypeEditorHost` and the bridge driver (`bridge/unity_client.py`) use the `Cuvara.UIToolkit.Prototype` names. The
  bridge plan's step 2 (`[Obsolete]` aliases in the bridge) is optional now that the target names exist here.
- The bridge writes `flow.json` v1 and has no schema in its own `schema/`; it can adopt
  [prototype-flow.schema.json](prototype-flow.schema.json) as the contract of its writer.
