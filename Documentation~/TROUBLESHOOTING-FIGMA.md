# Troubleshooting the Figma integration

Problems with Unity's **UI Toolkit Connector for Figma** (`com.unity.ui.figma`, tested with
`0.3.0-exp.1`) and with this package's integration around it. Setup and features:
[FIGMA-INTEGRATION.md](FIGMA-INTEGRATION.md). Validator rules and CLI:
[FIGMA-VALIDATION.md](FIGMA-VALIDATION.md).

The connector's own troubleshooting page (in its package, `Documentation~/troubleshooting.md`)
covers connector-only issues such as send-to-Figma and caption resolution. Its **Last import**
section in **Window > UI Toolkit > Connector for Figma design** is the first place to look after a
push that did not produce what you expected.

Where diagnostics appear:

- **Window > Cuvara > Figma Diagnostics**: per profile, the errors and warnings of the import
  validator (`FIG0xx`) and of the profile checks (`cuvara.*`).
- **Console**: one summary line per profile after each push, and one line per diagnostic after
  **Tools > Cuvara > Figma > Apply Cuvara Mapping Preset** or **Regenerate Token Bridges**. The
  format is `<context>: [<code>] <message> (<file>)`. Problems are logged as warnings, never as
  errors.
- **CLI**: `UxmlCodegenCli validate` prints `File(Line): severity Code: message`.

## The Cuvara Figma menus are missing

**Symptoms.** There is no **Window > Cuvara > Figma Diagnostics**, no **Tools > Cuvara > Figma**
menu, and no **Assets > Create > Cuvara > UI Toolkit > Figma Profile**.

**Cause.** These live in the gated assembly `Cuvara.UIToolkit.Editor.Figma`. It compiles only
when `com.unity.ui.figma` **0.3.0-exp.1 or later** is installed: its `versionDefines` sets
`CUVARA_UITOOLKIT_FIGMA` and its `defineConstraints` requires that define. This is by design;
without the connector the rest of the package works as before.

**Fix.**

1. Open **Window > Package Manager** and check that **UI Toolkit Connector for Figma design**
   (`com.unity.ui.figma`) is installed, at `0.3.0-exp.1` or later. If not, install it by name
   (see [FIGMA-INTEGRATION.md](FIGMA-INTEGRATION.md#setup)).
2. Fix any compile error in the Console. An assembly with errors anywhere in the project can
   keep the Editor from loading new menus.
3. The ungated parts do not need the connector: the validator CLI, the `Cuvara.UIToolkit.FigmaCore`
   C# API and the Screen Creator **Existing UXML** source.

## The Figma plug-in cannot connect to Unity

The connector connects the Figma plug-in to the Editor through the Unity Pipeline package.

| Symptom | Cause | Fix |
|---|---|---|
| The connector window has no **Copy token** button | the Pipeline server is not running | **Edit > Project Settings > Pipeline > Editor**: start the server |
| The plug-in keeps searching and never connects | **Allow Browser Clients** is off, or the server was not restarted after you turned it on | turn it on in **Edit > Project Settings > Pipeline > Editor**, restart the Pipeline server, copy the token again |
| The pasted token is empty or rejected | Unity clears the token from the clipboard **60 seconds** after **Copy token** | select **Copy token** again and paste it within 60 seconds |
| A connection that worked stops working | the Editor restarted (new token), or a script recompile moved the Pipeline server to another port (the token carries the port) | select **Copy token** again and reconnect in the plug-in's **Settings** tab |
| Pushes reach the wrong project | the plug-in connects to one Editor at a time | paste the token of the Editor you want |

### The headless helper fails

`Tools~/FigmaImport/figma-import.py` talks to the same Pipeline server
([FIGMA-VALIDATION.md](FIGMA-VALIDATION.md#headless-import-helper)).

- **Port file not found**: the Editor is not open on that project, or the connector is not
  installed. Pipeline writes `Library/Pipeline/.unity-pipeline-port` while the Editor runs.
- **Warning that the heartbeat is older than 60 s**: the Editor is probably closed. Open it.
- **HTTP 401**: the Editor restarted and the token changed. Run the script again.
- **State `expired`**: the Figma plug-in did not pick up the request within about 30 s. Open
  the plug-in on the file the link points to, connected to this Editor.
- **Status reports `waitingFor: figma_visible`**: Figma is minimised or in a background tab.
  Bring it to the front.

## Validator diagnostics (`FIG0xx`)

The rules are described in [FIGMA-VALIDATION.md](FIGMA-VALIDATION.md#rules). Most findings come
from the design, so fix them in Figma and push again: the next push rewrites every generated file.

| Code | What to do |
|---|---|
| `FIG001` manifest missing | Nothing has been imported into this folder, or the manifest was deleted. Deleting it unlinks the folder from its Figma file. Restore it from version control, or delete the generated files and push the file again. |
| `FIG002` manifest not JSON | Restore `.figma-manifest.json` from version control (the connector also keeps a `.bak`), then push again. Never edit it by hand. |
| `FIG003` screen UXML missing | The manifest lists a screen whose `.uxml` is gone. Push that frame again, or restore the file. |
| `FIG004` generated file missing | A file the connector wrote was deleted. Push the screen again. |
| `FIG005` UXML not well formed | A generated `.uxml` was edited or truncated. Revert it and push again. |
| `FIG006` USS not well formed | Same as `FIG005`, for a `.uss`. |
| `FIG007` folder missing | Wrong path. Pass the connector's folder, normally `Assets/Figma/<file>`. |
| `FIG010` class prefix | The connector's **USS class prefix** differs from the profile's (or `--prefix`). See [Two games, two prefixes](#two-games-two-prefixes). |
| `FIG020` literal colour | The Figma property is not bound to a variable, so it imports as a literal. Bind it to a colour variable in Figma. Turn the rule off with `--no-token-colors` if your project accepts literals. |
| `FIG021` summary | Count of `FIG020`. No action. |
| `FIG022` undefined token | See [Tokens do not resolve](#tokens-do-not-resolve). |
| `FIG030` too many inline styles | Above `--max-inline`. Non-component elements carry their whole style inline; turn repeated parts into Figma components so they get a class. |
| `FIG031` layout summary | Counts per `.uxml`. No action. |
| `FIG032` too many absolute elements | Above `--max-absolute`. Frames without auto layout, and `ABSOLUTE` children, import as `position: absolute`. Use auto layout in Figma. |
| `FIG040` unresolved `url()` | An image is missing from `images/`, or a `project://` path points to a moved file. Push the screen again. |
| `FIG041` no `Assets` ancestor | The folder is not inside a Unity project, so `project://` paths were not checked. Validate the folder inside its project. |
| `FIG050` `<Style src>` missing | A linked sheet (`tokens.uss`, a mode sheet, the screen `.uss`) is gone. Push again. |
| `FIG051` `<Template src>` missing | A component template `.uxml` is gone. Push the screens that use the component again. |

## Profile diagnostics (`cuvara.profile.*`)

| Code | Severity | What to do |
|---|---|---|
| `cuvara.profile.connector-folder-invalid` | error | Set **Connector folder** to a folder under `Assets/`, for example `Assets/Figma/MyGame`, not `Assets` itself. |
| `cuvara.profile.connector-folder-missing` | warning | The folder does not exist yet. Push the Figma file once, or fix the path. |
| `cuvara.profile.manifest-missing` | warning | No `.figma-manifest.json` in the folder: nothing was imported here, or the folder was unlinked. |
| `cuvara.profile.manifest-unreadable` | error | Restore the manifest from version control. |
| `cuvara.profile.validator-failed` | error | The validator threw. Report it with the message; the window keeps working. |
| `cuvara.profile.mode-sheet-missing` | warning | A **Mode sheets** entry names a file the connector did not write. Use the file names in the connector folder, such as `dark.uss`. |
| `cuvara.profile.screens-folder-invalid` | warning | **Screens folder** must be under `Assets/` and outside every connector folder. |

## The token bridge refuses its folder

`cuvara-theme-bridge.uss` is a game-owned file. The bridge writes nothing when one of these
errors is present.

| Code | What to do |
|---|---|
| `cuvara.bridge.folder-missing` | Set **Bridge output folder** on the profile, for example `Assets/UI/Theme`. |
| `cuvara.bridge.folder-not-game-owned` | The folder must be under `Assets/`. `Packages/` and paths that climb out with `..` are refused. |
| `cuvara.bridge.folder-inside-connector` | The folder is inside a connector import folder: a profile's connector folder, or any folder whose parent chain holds a `.figma-manifest.json`. Choose a folder outside `Assets/Figma/<file>/`. |

Other bridge diagnostics:

| Code | Severity | What to do |
|---|---|---|
| `cuvara.bridge.role-invalid` | error | A role is not a `--cuvara-<name>` variable in lowercase. Use a name from [THEMING.md](THEMING.md#tokens). |
| `cuvara.bridge.role-duplicate` | error | A role is listed twice; the first row is used. Delete the other row. |
| `cuvara.bridge.figma-token-invalid` | error | The Figma token is not a USS custom property name. Use the connector's name, for example `--brand-primary`. |
| `cuvara.bridge.figma-token-unknown` | warning | The token is not in the manifest, `tokens.uss` or the mode sheets. The alias is still written. Check the name (see [Tokens do not resolve](#tokens-do-not-resolve)). |
| `cuvara.bridge.tokens-unverified` | warning | No manifest or `tokens.uss` could be read, so nothing was checked. Push the file once. |
| `cuvara.bridge.no-roles` | warning | No role maps to a token; the bridge declares nothing. Fill **Token roles**. |
| `cuvara.bridge.role-unmapped` | info | The role keeps its theme default. No action unless you want it themed. |

## The mapping preset reports an error

**Tools > Cuvara > Figma > Apply Cuvara Mapping Preset** edits the connector's active mapping
asset (`FigmaImportSettings`) by property name.

| Code | Severity | What to do |
|---|---|---|
| `cuvara.mapping.property-missing` | error | The mapping asset has no `controlMap`, `controlMap[].figmaName`, `controlMap[].uitkControl` or `classPrefix` property. The installed connector is probably not `0.3.0-exp.1`, and a field was renamed. **Nothing was changed.** Check the version in the diagnostics window; until this package supports that version, add the row by hand on the connector's settings page. |
| `cuvara.mapping.connector-script-missing` | error | The connector's `FigmaImportSettings` script (GUID `0ced1937e6a344ac0a9f905cc9237a5b`) was not found. Install the connector at the tested version. |
| `cuvara.mapping.no-asset` | warning | No mapping asset exists. **Edit > Project Settings > UI Toolkit > Connector for Figma design** > **Create mapping**, then apply the preset again. |
| `cuvara.mapping.cleared` | info | The mapping was cleared on purpose in the connector settings. Select an **Active mapping** there to use the preset. |
| `cuvara.mapping.prefix-differs` | warning | See [Two games, two prefixes](#two-games-two-prefixes). The preset never overwrites a prefix. |
| `cuvara.mapping.control-unresolved` | error | A `uitkControl` with a `.` names a type that is not a `VisualElement` in the project. The connector falls back to a default element. Fix the type name, or add the assembly that defines it. |
| `cuvara.mapping.control-not-uxml-element` | warning | The mapped type has no `[UxmlElement]`, so UXML cannot create it. Add the attribute to the class (and make it `partial`). |
| `cuvara.mapping.rows-added` | info | Rows were added. A second run adds nothing. |

A row whose Figma name already matches (case and punctuation ignored, so `Safe Area`,
`safe-area` and `SafeArea` are one name) is never changed. If a user row maps `SafeArea` to
another control, that row wins.

## Text uses the wrong font

The connector resolves a Figma font family through the mapping asset's **Font map**, then by
a project font whose name contains the family. When neither matches, it drops
`-unity-font-definition` and the text uses the panel's default font. The connector window's
**Last import** section lists the unmatched families; a headless import reports them in
`result.notes`.

Fix: add the family to **Font map** (**Edit > Project Settings > UI Toolkit > Connector for
Figma design**), then push again. The Cuvara validator does not check fonts yet: the manifest
carries no font notes.

## Images look stretched, blurry or wrong

The plug-in rasterises vectors and image fills to PNG. They import as plain textures
(`npotScale = None`), not sprites and not vector images.

- **Scaling.** A Figma image fill in **Fill** mode imports as
  `-unity-background-scale-mode: scale-and-crop`, **Fit** as `scale-to-fit`, and **Tile** as a
  repeated background. A host rule that sets `stretch-to-fill`, or an element resized away from
  the Figma aspect ratio, distorts it. Size the element in Figma with the constraints you want.
- **No 9-slice.** The PNG is a texture with no sprite borders, so a panel designed to stretch
  around its corners scales as a whole. Use a separate sprite asset with borders in a
  game-owned USS file.
- **Blurry or stale.** The PNG is rendered at export time. A changed token colour does not
  update an already exported vector; push the frame again.
- **Icons all one colour.** Each icon component exports as one PNG that every instance shares,
  so per-instance colour overrides are lost. Use one icon component per colour.

## Tokens do not resolve

Symptoms: `FIG022`, `cuvara.bridge.figma-token-unknown`, or the built-in dialog and toasts keep
the default colours although the bridge sheet is attached.

- **Name.** The connector names a token `--` + the lowercased variable name with runs of
  non-alphanumerics collapsed to `-`, without the collection name: `Background/Primary` in
  collection `Colors` becomes `--background-primary`. Use that name in the profile.
- **Variable type.** Only colour and number variables import. STRING and BOOLEAN variables are
  dropped, so a token mapped to one never exists.
- **Not bound.** A Figma property that is not bound to a variable imports as a literal and
  cannot be themed (`FIG020`). Bind it in Figma.
- **Sheet order.** The bridge only aliases (`--cuvara-color-primary: var(--brand-primary)`), so
  `tokens.uss` must be attached at the same root. Set `RootUIDocument.themeOverrides` to
  `[tokens.uss, cuvara-theme-bridge.uss]` and `themeModeSheet` to the mode sheet. The
  connector's `<Style src="tokens.uss"/>` links sit on each imported screen, a subtree; the
  dialog, toasts and scrim live in the overlay layer, outside it. See
  [THEMING.md](THEMING.md#overriding-from-a-game-the-bridge-sheet).
- **Stale bridge.** The diagnostics window shows bridge freshness. **Regenerate bridge** after
  you change the profile.
- **Two variables, one name.** On a name collision the connector keeps the first variable and
  adds a note. Rename one in Figma.

## Two games, two prefixes

**Symptoms.** `FIG010` on one game's import, or `cuvara.mapping.prefix-differs` when you apply
the preset.

**Cause.** The connector's **USS class prefix** is one setting in the active mapping asset, and
one mapping asset is active for the whole project. Two profiles that expect different prefixes
cannot both be satisfied at the same time. The menu command applies a prefix only when all
profiles that set a prefix agree on it; the preset never overwrites an existing prefix.

**Fix.** Choose one:

- Share one prefix across the games in this project.
- Before you push a file, set the connector's prefix to that game's prefix (connector settings,
  or the diagnostics window's **Apply preset with this prefix** when the prefix is empty). The
  next push writes classes with the new prefix; files you do not push again keep the old one.
- Keep the games in separate Unity projects.

## Screen Creator refuses the output folder

The **Create Screen** wizard shows "The output folder is inside a Figma import folder". The
folder, or a parent up to the project root, holds a `.figma-manifest.json`. The connector owns
that folder and rewrites it on each push. Choose a game-owned folder, for example the profile's
**Screens folder**. See [MIGRATION-0.8.md](MIGRATION-0.8.md#3-screen-creator-and-figma-import-folders).

## The imported screen has no element names

The connector never turns Figma layer names into UXML `name` attributes, so `Require<T>("name")`
and the typed UXML codegen find nothing in an imported UXML. Typed bindings keyed on Figma node
ids are planned (gap G4) and not in this release. Until then, query by structure, or wrap the
imported UXML in a game-owned UXML that instantiates it and names the parts you need.
