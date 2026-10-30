# UI Toolkit Connector for Figma: compatibility audit and gap analysis

Audit of Unity's official connector `com.unity.ui.figma` against `com.cuvara.uitoolkit` 0.7.2.
Date: 2026-10-30. Source read: `com.unity.ui.figma@0.3.0-exp.1` and `com.unity.pipeline@0.9.1-exp.1`,
unpacked from `packages.unity.com` with `npm pack`. File references below point into those packages.

The purpose of this page is to decide what this package builds, and what it takes from the connector
unchanged. The rule is: **the connector imports, this package integrates.** No second importer, REST
converter, binding system or code generator.

## 1. Versions and compatibility

| Item | Value | Status |
|---|---|---|
| Connector versions | `0.1.0-exp.1`, `0.2.0-exp.1`, `0.3.0-exp.1` (latest) | experimental, API may change |
| Connector `unity` field | `6000.3` | client is on 6000.3.9f1: compatible |
| Dependencies | `com.unity.pipeline 0.9.1-exp.1`, `com.unity.nuget.newtonsoft-json 3.0.2`, `jsonserialize`, `imageconversion`, `uielements` | none conflict with this package |
| Assembly | `Unity.UIToolkit.Figma.Editor`, Editor-only, `autoReferenced: false` | |
| Unity-gated features | `gap` 6000.7.0a4, `backdrop-filter` 6000.6.0a6, `filter: drop-shadow()` 6000.6, grid 6000.7.0b3, gradients 6000.7.0b5 | on 6000.3: grids keep fixed cell size and position, gradients import as images; `filter: blur()` works (corrected 2026-10-30 from `FigmaToUss.cs`, which gates only `drop-shadow()`, not `blur()`) |
| Figma side | plugin "Unity UI Toolkit Connector" (Community), free, Dev or Full seat | Dev seat is read-only import |
| Multiple modes per collection | Figma paid plans (incl. Education) | Free plan gets one mode only |

Tested target for this integration: connector `0.3.0-exp.1`. Older connectors write a different file
shape (0.3.0 rewrites generated files on the first push after upgrade).

## 2. What the connector does (and how to observe it)

**Public API: none.** Every type in `Editor/` is internal. `InternalsVisibleTo` names only its own test
assembly (`Editor/AssemblyInfo.cs`). There are no public events or callbacks. A third-party assembly
can reference `Unity.UIToolkit.Figma.Editor` and still use nothing at compile time. **Integration must go
through files and commands, never through connector types.** This is also what keeps our side
compilable when the connector changes.

**Output folder.** `Assets/Figma/<SanitizeName(file name)>/` by default. The root and per-file folders live in
`ProjectSettings/FigmaOutputFolders.asset` (`m_Root`, `m_Files[]`); a file keeps its first folder. One folder holds:

- `<Screen>.uxml` and `<Screen>.uss` per frame; `<Template>.uxml` and `.uss` per used component;
- `tokens.uss` (`:root { --x: … }`) and `<mode>.uss` per variable mode;
- `images/<key>.png` (vectors and image fills are rasterised by the plugin);
- `.figma-manifest.json` (hidden ledger, written atomically, with `.bak`).

**Re-push.** Every generated file is rewritten. Deletion removes only files this screen generated
before and no longer generates, and only `.uxml`, `.uss` and `.png`. Files the connector never wrote are left
alone, including any `.cs`. A fixed-name clash (a foreign `tokens.uss`) refuses the import.

**Manifest (`FigmaSyncManifest`).** `fileName`, `tokens[]`, `screens[]`, `matchedMappings[]`, `foreign[]`.
Each screen: `assetName`, `rootNodeId` (stable across renames), `contentHash`, `generated[]`, `nodes[]`,
`components[]`, `states[]`, `templateNames[]`, `classNames[]`. Each node record: `figmaNodeId`, `doc`
(screen or template name), `path` (structural path, `"1/2/1"`), `tag`, `classes`, `props[]`.
`JsonUtility` format. **This is the only durable machine-readable description of an import.**

**Naming.**
- File names: anything outside `[A-Za-z0-9_-]` becomes `-`.
- Classes are emitted only for component masters: sanitised, lowercased, prefixed with the
  **USS class prefix** setting, and deduplicated `-2`, `-3` stably by component key.
- **Layer names never become `name` attributes.** The only `name` emitted is `text-<i>` inside
  templates, used for `AttributeOverrides`. Mapping rows cannot set `name`, `class` or `style`.
- Non-component elements carry their **whole style inline** (`style="…"`). Instances carry a class plus an inline delta.
- Stylesheets are linked by bare relative path: `<Style src="tokens.uss"/>`, mode sheet, `<Style src="Screen.uss"/>`.

**Tokens.**
- The token name is `--` + the lowercased variable name with runs of non-alphanumerics collapsed to `-`. The collection name is not included.
- Name collisions: the first one wins and a note is added.
- Values:
  - colour → `rgba()`;
  - number → `px`, unitless for font-weight, a fraction for opacity;
  - alias → `var(--target)`;
  - STRING and BOOLEAN variables are dropped.
- **Bound properties become `var(--token)`. Unbound properties stay hard-coded literals.**
- Each screen links only the mode it resolved to.

**Images and fonts.**
- PNGs are imported as plain textures (`npotScale = None`), not sprites.
- They are referenced as `url("project://database/…?guid=…")`.
- Fonts resolve through the mapping asset's font map, then by file-name match. A missing font drops `-unity-font-definition` and adds a note.

**Layout.**
- Auto-layout becomes flex.
- Frames without auto layout, and `ABSOLUTE` children, become `position: absolute; left; top`.

**Component mapping.** `FigmaImportSettings` ScriptableObject (default `<root>/Figma Mapping.asset`, script
GUID `0ced1937e6a344ac0a9f905cc9237a5b`). The active asset is chosen in `ProjectSettings/FigmaMappingState.asset`.

Fields:
- `classPrefix`;
- `fontMap[] {family, font}`;
- `controlMap[] {figmaName, uitkControl, bindings[] {uxmlAttribute, source, value}}`, where `source` is one of `FixedValue=0`, `ComponentProperty=2`, `DesignText=3`, `DesignIcon=4`.

Resolution order: plugin tag (built-in controls only) → mapping asset → name convention (components only) →
type default. **A fully qualified custom type such as `Cuvara.UIToolkit.Utilities.SafeAreaElement` is supported
through the mapping asset only.** The asset is plain Unity YAML, so `SerializedObject` can read and write it without a compile reference.

**Headless import for agents.** Pipeline serves HTTP on `127.0.0.1:7800-7849`. Port and token are in
`Library/Pipeline/.unity-pipeline-port` (`port`, `evalToken`). Call `POST /api/exec` with `Authorization: Bearer`
and no `Origin` header. Commands:
- `figma_import_url {url, output_folder?, headless=true}`: needs the Figma plugin open and connected on that file;
- `figma_import_status {request_id}`: `queued|exporting|importing|done|failed|expired`;
- `figma_import {payload, …}`.

The result (`FigmaImportResult`) has `ok`, `code`, `files[]`, `removed[]`, `notes[]`, `facts[]`, `inputWarnings[]` and `elapsedMs`.
Headless imports do not write the window's last-import report. The command result and the manifest are the record.

## 3. Capability matrix

| Concern | Connector | This package today | Decision |
|---|---|---|---|
| Figma → UXML/USS conversion | yes | no | **Reuse.** Never duplicate |
| Design tokens → USS custom properties | yes (`tokens.uss`, mode sheets) | no theme contract | **Build** the Cuvara theme contract and a token bridge (aliases only) |
| Figma component → control | yes (mapping asset, FQ types) | `SafeAreaElement` is `[UxmlElement]` | **Build** a Cuvara mapping preset applied through `SerializedObject` |
| Font mapping | yes (font map) | none | **Reuse.** Diagnostics report missing fonts |
| Images | PNG textures | none | **Reuse.** Diagnostics only |
| Element names for code | no (layer names dropped) | name-based codegen (`UxmlBindingGenerator`) | **Deferred** (G4): node-id bindings once runtime instance paths are confirmed |
| Typed view bindings `.uxml.g.cs` | no | yes, enrolled by `.g.cs` existence | **Extend** the existing codegen core and CLI. No new generator |
| Screen/Presenter scaffolding | no | `ScreenCreatorWizard` | **Extend.** The wizard can target an imported UXML |
| Import report | window only, session-scoped | none | **Build** persistent diagnostics from the manifest and command result |
| Standards validation | USS parse errors only | CI gates on package USS | **Build** a validator for imported output against package rules |
| Headless / agent import | yes (Pipeline HTTP) | none | **Build** a thin `Tools~` helper script. No new protocol |
| Built-in package UI themable | n/a | inline colours in `ConfirmDialog`, `ToastService`, `ScreenNavigator` scrim | **Build** classes plus `--cuvara-*` variables |
| Multi-game / multi-file | one folder per Figma file | n/a | **Build** a per-game `FigmaProfile` asset |
| Write-back to Figma | yes (styles and tokens only) | n/a | **Reuse.** Our tools must not edit connector files, or write-back sees the edits as user changes |

## 4. Gaps to build (in scope)

G1 **Theme contract (Runtime, no Figma dependency).**
- `Runtime/Theme/CuvaraTheme.uss` declares semantic `--cuvara-*` variables whose defaults equal today's inline values.
- `ConfirmDialog`, `ToastService` and the navigator scrim switch to `cuvara-*` classes.
- A host-side hook attaches theme and mode sheets to the root. With no override, rendering is unchanged.

G2 **Token bridge.**
- The per-game profile maps `--cuvara-*` roles to Figma tokens.
- A generated, game-owned `cuvara-theme-bridge.uss` holds aliases only (`--cuvara-color-surface: var(--background-primary);`).
- It is written **outside** the connector folder. `tokens.uss` is never edited.

G3 **Component mapping preset.**
- An Editor command adds missing `controlMap` rows for Cuvara controls to the active `FigmaImportSettings`, through `SerializedObject`.
- It is idempotent and never removes user rows.
- It also validates that `uitkControl` types resolve through `TypeCache`.

G4 **Node-id bindings (DEFERRED 2026-10-30, until a real import confirms runtime instance paths).** Layer names do not survive the import, so bindings key on the Figma node id, which is stable.
- A game-owned sidecar `<Screen>.figma-bindings.json` holds `{ bindingName: figmaNodeId }`. Agents fill it from Figma MCP `get_metadata`; people fill it from the diagnostics window.
- The manifest resolves `figmaNodeId` → `(doc, path, tag)` on every import.
- The existing codegen core gains a path-based query mode. It emits the same partial-class shape (`AssignQueries`, typed properties), resolved through a new runtime helper `RequireAtPath`.
- Output goes to `Generated/<Screen>.uxml.g.cs` beside the sidecar, in the game's folder. Drift checking covers these files.

G5 **Validator (Unity-free core plus CLI verb).** Checks imported UXML/USS and the manifest against package rules:
- unbound literal colours and sizes where a token exists (token coverage);
- inline-style element count;
- absolute-positioning count;
- class prefix ≠ profile prefix;
- unresolved `url()` references;
- binding sidecar entries whose node id is missing from the manifest (error);
- images imported as non-sprite where a sprite is needed (warning);
- missing fonts, from the manifest notes or the command result.

G6 **Diagnostics window.**
- Shows connector, Pipeline and Unity versions against the tested range, and whether a mapping asset is active.
- Shows the prefix, per-profile validation, token coverage and binding status.
- Offers apply-preset and regenerate-bindings actions.
- Re-runs on manifest change, through an `AssetPostprocessor` on `.figma-manifest.json` and `*.uxml` under profile folders.

G7 **Per-game `FigmaProfile` ScriptableObject.**
- Fields: connector folder, expected class prefix, token role map, bindings folder, namespace, mode sheets.
- Several profiles cover several games or files.

G8 **Headless import helper** (`Tools~`): a small script that reads the port file and calls `figma_import_url` / `figma_import_status`.

G9 **ScreenCreator source option.** The wizard can create View, Presenter and Model for an existing (imported) UXML instead of writing a new one.

## 5. Out of scope / skipped

- Any conversion, any REST or Figma-token client, and any edit of files inside the connector folder.
- Write-back features, and structural sync (the connector does not support it).
- Unity 6000.7-only features (grid, gradients, `gap`), until the client moves to 6000.7.

## 6. Isolation

- Runtime theme (G1) and `RequireAtPath` are plain UI Toolkit: no connector reference.
- Unity-free core (G5 validator, manifest model; later G4) lives under `Editor/FigmaCore/` in the **ungated** `Cuvara.UIToolkit.Editor` assembly. It is compiled into the `Tools~` CLI too, like `Editor/Codegen/Core/`. It reads connector *files*, never connector types, so it needs no gate, and package CI tests it without the connector installed.
- Unity Editor parts (G2, G3, G6, G7, G9) live in `Cuvara.UIToolkit.Editor.Figma`.
  - The assembly has **no reference** to `Unity.UIToolkit.Figma.Editor`.
  - It is gated by `versionDefines` on `com.unity.ui.figma` → `CUVARA_UITOOLKIT_FIGMA`, and `defineConstraints` on that define.
  - Without the connector it does not compile in, and the package works as before.
- The CLI and validator work without Unity and without the connector, so CI and agents can check committed output.

## 7. Experimental-API risks

| Risk | Mitigation |
|---|---|
| Manifest schema changes | tolerant reader (unknown fields ignored, missing ones reported); diagnostics shows the tested connector version |
| Structural `path` semantics change | bindings key on node id; the path is re-resolved on each import; the validator fails loudly on unresolved ids |
| Mapping asset field rename | the preset uses `SerializedObject.FindProperty`, and a missing property is a diagnostics error, not an exception |
| Pipeline port/token protocol change | the helper script is isolated in `Tools~`, never shipped to runtime |
| Runtime path of `<ui:Instance>` children (TemplateContainer level) | must be confirmed against a real import before G4 is released; a test fixture records the expected tree |
| 0.x minor bumps rewrite generated files | regenerate bindings after an upgrade; drift check catches stale `.g.cs` |
| Inline `var(--token)` on non-component layers (0.3.0-exp.1 writes bound properties of plain layers inline). UI Toolkit 6000.3 cannot clone it: `NullReferenceException` in `StyleVariableResolver.ResolveVariable` (verified 2026-10-30 on 6000.3.9f1) | designer rule: bind variables on components only (values land in class rules); the inline-var validator rule fails the import folder before a screen is opened; documented in FIGMA-INTEGRATION.md and TROUBLESHOOTING-FIGMA.md |

## 8. Implementation status (2026-10-30, branch `feat/uitoolkit/figma-connector`, target release 0.8.0)

User documentation for the built parts: [FIGMA-INTEGRATION.md](FIGMA-INTEGRATION.md) (setup,
profiles, bridge, preset, diagnostics, supported features),
[FIGMA-VALIDATION.md](FIGMA-VALIDATION.md) (G5, G8), [THEMING.md](THEMING.md) (G1),
[TROUBLESHOOTING-FIGMA.md](TROUBLESHOOTING-FIGMA.md), [MIGRATION-0.8.md](MIGRATION-0.8.md),
and [FIGMA-REAL-IMPORT.md](FIGMA-REAL-IMPORT.md) (compatibility record against the connector source,
saved import results, unsupported constructs, real-import lab). The **Figma Import** sample
(`Samples~/FigmaImport`) shows a game adopting the integration with a stand-in import; it runs
without the connector. A real import is reproducible with `Tools~/FigmaLab/create-lab-project.py`
and the lab procedure; its evidence checklist is still to be filled from a recorded push.

| Gap | Status | Where |
|---|---|---|
| G1 theme contract | done | `Runtime/Theme/`, `Documentation~/THEMING.md`. Imported screens follow the mode through `ICuvaraThemeService.RegisterModeSheets` and `AttachScreen` |
| G2 token bridge | done | `Editor/Figma/FigmaTokenBridge.cs` |
| G3 mapping preset | done | `Editor/Figma/FigmaMappingPreset*.cs` |
| G4 node-id bindings | deferred | still waits for a recorded real import to confirm the runtime instance paths; no node-id binding code exists |
| G5 validator | done, partial | `Editor/FigmaCore/`, CLI `validate`, `Documentation~/FIGMA-VALIDATION.md`. Missing fonts and images that never arrived are reported from the saved import result of a headless import (`FigmaImportResultReader`, `cuvara.import.fact.MissingFonts` / `MissingImages` / `ImagePending`), not from the folder; pushes from the Figma plug-in leave no saved result. Not yet: non-sprite image check, binding sidecar ids (G4), `project://database/Packages/...` paths (not checked by design) |
| G6 diagnostics | done | `Editor/Figma/FigmaDiagnosticsWindow.cs`, `FigmaImportPostprocessor.cs` (Unity does not import dot-files, so a push triggers re-validation through the rewritten `.uxml`/`.uss`, not the manifest). The window also shows the last saved headless import result per profile (`FigmaImportResultStore.cs`, `Library/CuvaraFigma/`) |
| G7 profiles | done | `Editor/Figma/FigmaProfile.cs`. The connector class prefix is one project-wide setting, so two games with different prefixes in one project conflict |
| G8 headless import helper | done | `Tools~/FigmaImport/figma-import.py` (saves the import result; self-test `test_figma_import.py`); real-import lab `Tools~/FigmaLab/` |
| G9 ScreenCreator source | done | `Editor/ScreenCreator/` ("Existing UXML") |
