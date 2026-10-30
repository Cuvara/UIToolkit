# Real Figma import: compatibility, diagnostics and lab procedure

This page makes a real import through Unity's **UI Toolkit Connector for Figma**
(`com.unity.ui.figma` 0.3.0-exp.1) reproducible, and records what this package assumes about
the connector. It has four parts:

- **Compatibility**: every connector name this package depends on, with the source line it was
  checked against.
- **Import result diagnostics**: how a headless import's notes reach the diagnostics window.
- **Unsupported constructs**: what the connector drops or degrades on Unity 6000.3, and which
  diagnostic shows it.
- **Lab procedure**: a throwaway project, the import steps, and an evidence checklist.

The rule from [FIGMA-CONNECTOR-AUDIT.md](FIGMA-CONNECTOR-AUDIT.md) holds throughout: **the
connector imports, this package integrates.** Nothing here converts Figma data or edits a
connector file.

## Compatibility

Checked on 2026-10-30 against `com.unity.ui.figma@0.3.0-exp.1` and `com.unity.pipeline@0.9.1-exp.1`
(`npm pack` from `packages.unity.com`). Paths are relative to each package root. All connector
types are `internal`: this package reads them as files and serialized fields, never as types.

### Mapping asset (`FigmaImportSettings`)

| Assumption | Connector source | Used in | Status |
|---|---|---|---|
| Script GUID `0ced1937e6a344ac0a9f905cc9237a5b` | `Editor/Settings/FigmaImportSettings.cs.meta` | `FigmaMappingPreset.SettingsScriptGuid` | match |
| Field `fontMap` (`{family, font}`) | `Editor/Settings/FigmaImportSettings.cs:17`, `:143-145` | not edited; fonts reported from the import result | match |
| Field `classPrefix` | `Editor/Settings/FigmaImportSettings.cs:21` | `FigmaMappingPreset.PropertyClassPrefix` | match |
| Field `controlMap[]` | `Editor/Settings/FigmaImportSettings.cs:24` | `FigmaMappingPreset.PropertyControlMap` | match |
| Row `figmaName`, `uitkControl`, `bindings[]` | `Editor/Settings/FigmaImportSettings.cs:152`, `:155`, `:162` | `FigmaMappingPreset.Property*` | match |
| Legacy row fields `captionAttribute`, `iconAttribute` (hidden, folded into bindings on load) | `Editor/Settings/FigmaImportSettings.cs:158-159`, `:105-138` | cleared on rows the preset inserts | match |
| Binding `{uxmlAttribute, source, value}`, `source` 0/2/3/4 | `Editor/Mapping/PropertyBinding.cs:6-23` | the preset writes no bindings | match |
| Active asset: selection, else none if cleared, else first by sorted path | `Editor/Settings/FigmaImportSettings.cs:65-86` | `FigmaMappingPresetCommand.LocateActive` | match |

### Project settings files

| Assumption | Connector source | Used in | Status |
|---|---|---|---|
| `ProjectSettings/FigmaMappingState.asset`, field `m_ActiveMappingGuid`, `"-"` = cleared | `Editor/Settings/FigmaMappingState.cs:9`, `:13`, `:15` | `FigmaMappingPreset.ParseMappingState` | match. It is a text file only while the project's asset serialization is Force Text (the default); a binary file reads as "unset" |
| `ProjectSettings/FigmaOutputFolders.asset`, fields `m_Root`, `m_Files[] {fileName, folder}` | `Editor/Sync/FigmaOutputFolders.cs:25`, `:37-42` | documentation only; profiles name the folder explicitly | match |
| Output folder names: `[A-Za-z0-9_-]`, others become `-`, outer `-` trimmed | `Editor/Import/FigmaPathGuard.cs:125-140` | `FigmaImportResultReader.SanitizeName`, `figma-import.py sanitize_name` | match |

### Manifest (`.figma-manifest.json`)

| Assumption | Connector source | Used in | Status |
|---|---|---|---|
| File name `.figma-manifest.json`, backup `.bak` | `Editor/Sync/FigmaManifestIO.cs:14-15` | `FigmaManifestReader.ManifestFileName`, `FigmaPaths.ManifestFileName` | match. The reader does not fall back to `.bak`; an unreadable manifest is an error in the window |
| `JsonUtility` format | `Editor/Sync/FigmaManifestIO.cs:66` | `FigmaJson` (tolerant reader) | match |
| Top level `fileName`, `tokens[]`, `screens[]`, `matchedMappings[]`, `foreign[]` | `Editor/Sync/FigmaSyncManifest.cs:47-53` | `FigmaManifestReader` reads `fileName`, `tokens`, `screens` | match |
| Screen `assetName`, `rootNodeId`, `generated[]`, `tokens[]` | `Editor/Sync/FigmaSyncManifest.cs:11-23` | `FigmaManifestReader` | match |
| Token record `name` (`--x`) | `Editor/WriteBack/FigmaTokenDiff.cs:10-17` | token bridge verification | match |

### Pipeline transport

| Assumption | Pipeline source | Used in | Status |
|---|---|---|---|
| Port file `Library/Pipeline/.unity-pipeline-port` | `Runtime/PipelineService/PipelineServiceDescriptor/EditorInstanceDescriptor.cs:24`, `:241` | `figma-import.py PORT_FILE` | match |
| `port` (int), `evalToken`, `endpoints[] {type, port}` (port as a string) | same file `:52`, `:94`, `:146` | `read_port_file` | match |
| `lastHeartbeat` is a Newtonsoft `DateTime` from `DateTime.UtcNow` (7 fraction digits, `Z`) | same file `:88`, `:189-190` | `parse_heartbeat` | **fixed**: Python 3.8-3.10 cannot read 7 fraction digits, so the staleness warning was silently skipped there. The helper now cuts the fraction to 6 digits |
| `POST /api/exec` body `{command, parameters, timeout}`, timeout in **milliseconds** | `Runtime/Models/CommandExecutionRequest.cs:24`, `:59`, `:66` | `Pipeline.exec` | match |
| Reply envelope `{success, result, error, errorDetails, errorCode, status, retryable}` | `Runtime/Models/CommandExecutionResponse.cs:18-81`, `Runtime/Models/BaseResponse.cs:32-39` | `Pipeline.exec` | match |
| Any `Origin` header is refused (403) unless Allow Browser Clients and `Origin: null` | `Runtime/Common/BasePipelineServer.cs:737-745` | helper sends no `Origin` (urllib) | match |
| `Authorization: Bearer <evalToken>` | `Runtime/Common/BasePipelineServer.cs:908-919` | `Pipeline.exec` | match |
| HTTP 504 timeout, 503 busy (retryable), **429 too many queued commands** | `Runtime/Common/BasePipelineServer.cs:2485`, `:2536`, `:2542` | `RETRY_STATUSES` | **fixed**: 429 was a hard error (exit 2); it now retries like 503/504, since nothing ran |
| Allow Browser Clients: `m_AllowBrowserClients` in `ProjectSettings/Packages/com.unity.pipeline/EditorPipelineConfig.json`, applies on next server start | `Editor/EditorPipelineConfig.cs:70`, `:170`; path documented in `Documentation~/commands/editor-lifecycle-and-observability.md` | lab Editor script | match |

### Connector commands

| Assumption | Connector source | Used in | Status |
|---|---|---|---|
| `figma_import_url {url, output_folder?, headless=true}` returns a request at once | `Editor/Pipeline/FigmaPipelineCommands.cs:70-87` | `figma-import.py` | match |
| `figma_import_status {request_id}`; waits behind a running import (pass a long timeout) | `Editor/Pipeline/FigmaPipelineCommands.cs:89-99` | `STATUS_TIMEOUT_MS = 120000` | match |
| `figma_import {payload, output_folder, headless=false, request_id, timeout}` (plugin protocol) | `Editor/Pipeline/FigmaPipelineCommands.cs:46-68` | not called | n/a |
| Request reply: `ok`, `requestId`, `state`, `fileName`, `nodeId`, `elapsedSeconds`; while open `retryAfterSeconds`, `next`, `waitingFor`; failed/expired `code`, `error`, `reason`; finished `result` | `Editor/Requests/FigmaRequestStore.cs:1001-1041` | polling loop, `result_document`, `FigmaImportResultReader` | match |
| States `queued`, `exporting`, `importing`, `done`, `failed`, `expired`; a result with `ok=false` makes the request `failed` with the result's code | `Editor/Requests/FigmaRequestStore.cs:129-134`, `:463-487` | `FINISHED_STATES`; exit 1 | match |
| Refusal before queueing: `{ok:false, code, error}` (+`openFileCount`, `project`), no `state` | `Editor/Requests/FigmaRequestStore.cs:52-63`, `:204-282` | refusal branch (now saved) | match |
| Unknown request: `{ok:false, code:"unknown_request", error}` (+`requestId`) | `Editor/Requests/FigmaRequestStore.cs:285-307` | refusal branch | match |
| Request codes `invalid_link`, `unsupported_link`, `frame_link_required`, `plugin_not_connected`, `file_not_open`, `ambiguous_file`, `too_many_requests`, `unknown_request`, `not_a_frame`, `node_not_found`, `push_failed`, `plugin_lost`, `expired` | `Editor/Requests/FigmaRequestStore.cs:16-31` | shown verbatim | match |
| Stored result drops `notes` (facts carry the same as data) and caps lists: 100 files, 50 removed/invalid, 25 facts, 10 items of 48 chars, 20 input warnings of 120 chars | `Editor/Requests/FigmaRequestStore.cs:116-125`, `:621-647` | reader uses facts first, notes only when there are none | match |

### Import result (`FigmaImportResult`)

| Assumption | Connector source | Used in | Status |
|---|---|---|---|
| `ok`, `code`, `error`, `schemaMismatch`, `pluginSchema?`, `editorSchemas?`, `converted`, `ussErrors`, `nodes`, `templates`, `uxml`, `folder`, `files[]`, `removed[]`, `invalidFiles[]`, `notes[]`, `facts[]`, `inputWarnings[]`, `elapsedMs`; paths are project paths | `Editor/Import/FigmaImportResult.cs:12-42` | `FigmaImportResultReader` | match. The audit (section 2) listed only part of these fields |
| Fact `{kind, severity: "warning" or "message", count, items[], counts?, extra?}` | `Editor/Import/FigmaImportResult.cs:47-73` | `FigmaImportResultFact` | match |
| Import error codes `invalid_json`, `invalid_field_type`, `missing_field`, `schema_mismatch`, `no_nodes`, `too_many_nodes`, `too_deep`, `invalid_parent_index`, `no_screen_root`, `too_many_files`, `too_many_modes`, `path_rejected`, `invalid_output_folder`, `folder_conflict`, `internal_error`, `files_not_imported`, `request_mismatch`; never renamed | `Editor/Import/FigmaImportError.cs:84-100` | shown verbatim | match |
| Note kinds (never renamed) and severities: `MultiMode`, `ScreenRenamed`, `MissingMasters`, `RenderedGradients` are messages, every other kind (and any new one) is a warning | `Editor/Mapping/FigmaImportNote.cs:117-150`, `Editor/Mapping/FigmaImportReport.cs:15-29` | `FactSeverity`; unknown severity reads as a warning | match |
| Variables: the wire carries only `COLOR` and `FLOAT`; STRING and BOOLEAN variables never arrive | `Editor/Protocol/FigmaWire.cs:49`, `:68` | token bridge | match |

## Import result diagnostics

A headless import leaves no record of its notes: the connector writes no window report for it,
and the manifest holds no notes. `figma-import.py` therefore saves the final result:

```
python3 Packages/com.cuvara.uitoolkit/Tools~/FigmaImport/figma-import.py "<frame link>" \
    [--save-result <path>] [--no-save-result]
```

- Default path: `<project>/Library/CuvaraFigma/last-import-<file>.json`, where `<file>` is the
  Figma file name sanitized with the connector's folder rule. `Library/` is never imported and
  never committed.
- Content: the connector's `FigmaImportResult` as `figma_import_status` returned it. For a request
  that never got one (refused, expired), the reply's `ok`, `code` and `error`. Beside it a
  `request` object with what only the helper knows: `requestId`, `state`, `code`, `error`,
  `reason`, `fileName`, `nodeId`, `url`, `savedAt` (UTC), `finished`.
- When `--max-wait` runs out while the request is still `queued`, `exporting` or `importing`, the
  helper exits 1 and still saves the attempt: `ok` false, no code, no import result,
  `request.finished` false and `request.waitedSeconds`. The request may still complete in Unity.
- A save failure is a warning; the exit code still describes the import. `--dry-run` saves nothing.

`Editor/FigmaCore/FigmaImportResultReader.cs` (Unity-free, also compiled into the CLI) turns a saved
result into diagnostics. **Window > Cuvara > Figma Diagnostics** shows, per profile, the newest saved
result whose `folder` is the profile's connector folder (or, for a refused request, whose Figma file
name matches the manifest's). It notes when the connector's manifest is newer than the saved
result (a later push from Figma replaced that import), and lists saved results that match no
profile. The window re-reads saved results when it gets focus.

| Code | Severity | When |
|---|---|---|
| `cuvara.import.summary` | Info | Always: file, time, state, files written and removed, duration |
| `cuvara.import.incomplete` | Warning | The helper stopped waiting while the request was still `queued`, `exporting` or `importing` (no result yet) |
| `cuvara.import.failed` | Error | `ok` is false on a finished request: the code, the error and the plugin's reason |
| `cuvara.import.schema-mismatch` | Error | `schemaMismatch`: plugin and connector speak different wire schemas |
| `cuvara.import.uss-errors` | Error | `ussErrors`: Unity reported errors in generated USS |
| `cuvara.import.file-not-imported` | Error | One per `invalidFiles` entry, pointing at the file |
| `cuvara.import.input-warning` | Warning | One per `inputWarnings` entry |
| `cuvara.import.fact.<Kind>` | connector's: `warning` is Warning, `message` is Info, unknown is Warning | One per fact, with its items |
| `cuvara.import.note` | Warning | One per `notes` entry, only when the result has no facts |
| `cuvara.import.result-unreadable` | Error | The saved file is not JSON, or has no `ok` field |

This closes the audit's G5 leftovers for **missing fonts** (`fact.MissingFonts`) and **images that
never reached Unity** (`fact.MissingImages`, `fact.ImagePending`) without guessing from the files.
Imports pushed from the Figma plugin (not through the helper) have no saved result; for those, the
connector window's **Last import** section is the record.

## Unsupported constructs

What the connector drops or degrades on Unity **6000.3**, from its troubleshooting page
(`Documentation~/troubleshooting.md`, "Imported design looks different from Figma"), its report
texts (`Editor/Localization/FigmaStrings.Report.cs`) and the converter
(`Editor/Mapping/FigmaImportReport.cs:40-66`). The connector's own `versionDefines` turn the Unity
6000.6/6000.7 features on (`Editor/Unity.UIToolkit.Figma.Editor.asmdef:17-38`); on 6000.3 none are on.

| Figma construct | On Unity 6000.3 | Surfaced by |
|---|---|---|
| Auto layout spacing (`gap`) | Dropped; children touch (needs 6000.7.0a4) | `fact.DroppedGap` |
| Negative auto layout spacing | Dropped; layers do not overlap | `fact.NegativeGap` (only where `gap` is supported) |
| Drop shadow on a frame or shape | Skipped (USS has no `box-shadow`); text shadows import | `fact.DroppedShadows` |
| Background blur | Dropped (needs 6000.6.0a6); screen-space panels only when supported | `fact.DroppedBackgroundBlur` |
| Gradient fills | Imported as images (fixed resolution, no token updates) or no fill | `fact.RenderedGradients` (message), `fact.DroppedGradients` |
| Grid layout | Fixed cell size and position (USS grid needs 6000.7) | `fact.GridAsFixedPositions` |
| Blend modes (layer, fill, stroke) | Not imported; drawn as Normal | `fact.BlendModes` |
| Text stroke | Not imported, or drawn centered, or thinner than in Figma | `fact.DroppedTextStrokes`, `fact.CenteredTextStrokes`, `fact.ClippedTextOutlines` |
| Text shadow under an outline | Shadow looks smaller | `fact.TextShadowUnderOutline` |
| Fonts not in the project or the font map | Default font; `-unity-font-definition` dropped | `fact.MissingFonts` |
| Vector and image fills | Rasterised PNG textures (not sprites, not vector); token colour changes do not reach them | `fact.ImagePending`, `fact.MissingImages`; validator for URLs |
| Per-instance icon colour overrides | All instances share one PNG | none (documented limitation) |
| Layers the plugin could not read | Missing | `fact.MissingLayers` |
| Variant states over 128 per push or 32 per component | Not imported | `fact.DroppedStates` |
| State styling on components with several text layers | Text styling per state not imported; variant styles skipped | `fact.DroppedTextStates`, `fact.SkippedStateVariants` |
| Unrecognised state names | No interaction style | `fact.UnmappedStates` |
| Components named almost like a control | Plain element | `fact.NearMissNames` |
| Layers a control cannot hold | Dropped | `fact.DiscardedLayers` |
| Tabs / radio buttons outside their container | Do not behave as tabs / a group | `fact.TabsOutsideTabView`, `fact.RadioButtonsOutsideGroup`, `fact.EmptyTabs` |
| Detached or unresolved component masters | Styling inline instead of a class | `fact.MissingMasters` (message); validator inline-style count |
| Several variables with one sanitized name | First wins | `fact.TokenCollision` |
| STRING and BOOLEAN variables | Never exported | none (wire carries COLOR and FLOAT only); token bridge flags unknown tokens |
| Layer names | Never become `name` attributes | none (by design; G4 deferred); see FIGMA-INTEGRATION.md |
| Unbound property values | Literals, not `var()` | validator token coverage |
| Structural edits in Unity | Never sent back to Figma | none (connector limitation) |

## Optional-assembly behaviour

Verified by reading (2026-10-30):

- `Editor/Figma/Cuvara.UIToolkit.Editor.Figma.asmdef` sets `CUVARA_UITOOLKIT_FIGMA` through
  `versionDefines` on `com.unity.ui.figma` `0.3.0-exp.1` (meaning that version or later) and lists it
  in `defineConstraints`. Without the connector the assembly is not compiled. The gated test
  assembly `Tests/Editor/Figma/Cuvara.UIToolkit.Editor.Figma.Tests.asmdef` repeats both.
- No file outside `Editor/Figma/` and `Tests/Editor/Figma/` names `Cuvara.UIToolkit.Editor.Figma`.
  No asmdef references `Unity.UIToolkit.Figma.Editor`. The one connector type name in the package,
  `Unity.UIToolkit.Figma.FigmaImportSettings`, is a string resolved through `TypeCache` in a gated
  test.
- `Editor/FigmaCore/` (including `FigmaImportResultReader`) has no Unity reference and compiles into
  `Tools~/UxmlCodegenCli`.
- With the connector present, the gated Editor sources and tests compile against the Unity
  6000.3.9f1 assemblies (harness build, 0 errors, 0 warnings).

## Lab procedure

A throwaway project for a real import. Nothing here touches another project.

### 1. Create the lab project

```
python3 <checkout>/Tools~/FigmaLab/create-lab-project.py C:/tmp/uitk-figma-lab --package <checkout>
```

It writes `Packages/manifest.json` with `com.cuvara.uitoolkit` as `file:<checkout>`, `com.unity.ui.figma`
`0.3.0-exp.1`, the CI test job's `com.unity.test-framework` `1.6.0`, `com.unity.inputsystem` `1.18.0`
and `com.unity.entities` `1.4.8` (`--no-entities` leaves it out), `com.cysharp.unitask` and
`jp.hadashikick.vcontainer` at the versions the package declares, and the OpenUPM scoped registry.
It also writes `ProjectSettings/ProjectVersion.txt` (`--unity`, default `6000.3.9f1`), a minimal
`ProjectSettings.asset` with the new Input System, and `Assets/Editor/CuvaraFigmaLab/CuvaraFigmaLabSetup.cs`.
It refuses a non-empty folder unless `--force`, and deletes nothing.

### 2. Open it and connect Figma

1. Open the folder with Unity **6000.3.x** (Unity Hub > Add > Add project from disk). Wait for the
   package resolve and the first compile. The Console must show no compile error.
2. The lab script logs once per session. If Pipeline's settings file already holds
   `m_AllowBrowserClients`, it sets it to `true`. Otherwise it prints the manual step: **Edit >
   Project Settings > Pipeline > Editor > Allow Browser Clients**. Either way, restart the Pipeline
   server on that page (the setting applies on the next start).
3. **Window > UI Toolkit > Connector for Figma design**, then **Copy token**. The token leaves the
   clipboard after 60 seconds.
4. In Figma (desktop or web), run the Community plugin **Unity UI Toolkit Connector**. On its
   **Settings** tab, paste the token into **Pipeline token** and select **Connect to Unity**.

### 3. Import a test frame

Use a frame that exercises the checklist below: auto layout with spacing, text in at least two
fonts (one not in the project), a bound colour variable and an unbound literal, an image fill, a
vector icon, a component used twice, a component named `SafeArea`, a drop shadow and a gradient.

1. Create a profile: **Assets > Create > Cuvara > UI Toolkit > Figma Profile**. Set the connector
   folder (default import folder: `Assets/Figma/<file>`), class prefix (for example `lab`) and a bridge
   folder such as `Assets/UI/Theme`.
2. **Tools > Cuvara > Figma > Apply Cuvara Mapping Preset** (create the connector's mapping asset on
   its Project Settings page first if it asks).
3. Import, either way:
   - In the plugin, select the frame and **Push**; or
   - headless: in Figma, **Copy link to selection** on the frame, keep the plugin open and connected,
     then run
     `python3 Packages/com.cuvara.uitoolkit/Tools~/FigmaImport/figma-import.py "<link>" --project C:/tmp/uitk-figma-lab --validate --prefix lab`.
     It prints the result, saves it to `Library/CuvaraFigma/`, and runs the validator.
4. Validator CLI (if the import was a push):
   `dotnet run --project <checkout>/Tools~/UxmlCodegenCli -- validate C:/tmp/uitk-figma-lab/Assets/Figma/<file> --prefix lab`.
5. **Window > Cuvara > Figma Diagnostics**: versions (tested), mapping, profile, validator results,
   token coverage, and the **Last import result** block (headless imports only).
6. Map token roles in the profile, then **Regenerate token bridges**.
7. **Assets > Cuvara > Create Screen**, Source **Existing UXML (e.g. Figma import)**, pick the imported
   `.uxml`. Register the view with its `AssetKey` and an `IVisualTreeAssetLoader` that resolves it.
8. Enter Play mode with a scene that shows the screen. Take a screenshot.

### 4. Evidence checklist

The Release Lead fills this in for the record. "Source" names where the evidence comes from.

| # | Check | Expected | Source | Result |
|---|---|---|---|---|
| 1 | Hierarchy | Frame children appear in the same order and nesting | `.uxml`, UI Toolkit Debugger | |
| 2 | Layout | Auto layout becomes flex; non-auto-layout children are absolute; spacing dropped on 6000.3 is reported | `.uss`/inline styles, `fact.DroppedGap` | |
| 3 | Typography | Mapped fonts render; unmapped fonts fall back and are named | screenshot, `fact.MissingFonts` | |
| 4 | Colours | Bound colours are `var(--token)`, unbound are literals and counted | `.uss`, validator token coverage | |
| 5 | Spacing and sizes | Padding, margins and sizes match Figma within 1 px | UI Toolkit Debugger vs Figma inspect | |
| 6 | Images | PNGs under `images/`, referenced through `project://database/...?guid=`; none blank | files, validator URL check, `fact.MissingImages` / `fact.ImagePending` | |
| 7 | Asset references | Every `url()` resolves; no Console import error | validator, Console, `cuvara.import.file-not-imported` | |
| 8 | Component mapping | `SafeArea` imports as `Cuvara.UIToolkit.Utilities.SafeAreaElement`; class prefix `lab-` | `.uxml`, diagnostics mapping section | |
| 9 | Theme | Bridge aliases resolve; `--cuvara-*` values follow the Figma tokens | `cuvara-theme-bridge.uss`, screenshot with a mode change | |
| 10 | Unsupported features | Every dropped feature in the frame (shadow, gradient...) is listed | diagnostics `fact.*` lines | |
| 11 | Re-push | A second push rewrites only generated files; profile, bridge and screens untouched | `git status` / file times | |
| 12 | Screenshot | Unity Play mode next to the Figma frame | attach both | |

Record the Unity version, connector and Pipeline versions (diagnostics window), plugin version (Figma
plugin settings) and the saved result file with the evidence.
