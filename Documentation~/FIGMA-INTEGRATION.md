# Figma integration

How to use Unity's **UI Toolkit Connector for Figma** (`com.unity.ui.figma`) with this
package. The rule: **the connector imports, this package integrates.** The connector turns
Figma frames into UXML, USS and token sheets. This package adds a per-game profile, a token
bridge to the Cuvara theme, a component mapping preset, diagnostics, and screen scaffolding
over the imported UXML. It never converts Figma data itself, and it never edits a file the
connector wrote.

The background, and the gap list the features below come from, is the connector audit
([FIGMA-CONNECTOR-AUDIT.md](FIGMA-CONNECTOR-AUDIT.md), gaps G2, G3, G6, G7, G9). When something
does not work, see [TROUBLESHOOTING-FIGMA.md](TROUBLESHOOTING-FIGMA.md). Upgrading from 0.7.2:
[MIGRATION-0.8.md](MIGRATION-0.8.md).

A complete, runnable example is the **Figma Import** sample (`Samples~/FigmaImport`, import it
from the Package Manager): an imported screen with a View and Presenter, the theme fed by Figma
tokens through a bridge, and a light/dark switch. It runs without the connector. A real import
in a throwaway project, step by step: [FIGMA-REAL-IMPORT.md](FIGMA-REAL-IMPORT.md).

## Requirements

| Item | Version |
|---|---|
| Unity | 6000.3 or later (the package's `unity` field; CI runs 6000.3.9f1) |
| `com.unity.ui.figma` (UI Toolkit Connector for Figma design) | `0.3.0-exp.1`, the tested version. It is experimental. |
| `com.unity.pipeline` | `0.9.1-exp.1`, installed by the connector |
| Figma | the Community plug-in **Unity UI Toolkit Connector**; a free, Dev or Full seat |

The connector is optional. Without it, everything in this package except the
`Cuvara.UIToolkit.Editor.Figma` assembly works as before.

## What lives where

| Part | Assembly | Needs the connector |
|---|---|---|
| Profile, token bridge, mapping preset, diagnostics window, import watcher | `Cuvara.UIToolkit.Editor.Figma` (`Editor/Figma/`) | yes |
| Validator and manifest reader | `Cuvara.UIToolkit.Editor` (`Editor/FigmaCore/`) | no |
| Screen Creator "Existing UXML" source | `Cuvara.UIToolkit.Editor` (`Editor/ScreenCreator/`) | no |
| Theme variables (`--cuvara-*`) | `Cuvara.UIToolkit` (Runtime) | no |

`Cuvara.UIToolkit.Editor.Figma` is Editor-only and gated: `versionDefines` sets
`CUVARA_UITOOLKIT_FIGMA` when `com.unity.ui.figma` **0.3.0-exp.1 or later** is installed, and
`defineConstraints` compiles the assembly only with that define. Without the connector the
assembly is not compiled and nothing else changes. It has no reference to the connector's
assembly (`Unity.UIToolkit.Figma.Editor`), whose types are all internal: it works through the
connector's files and its mapping asset only.

## Setup

1. Install the connector: **Window > Package Manager** > **+** > **Add package by name**, name
   `com.unity.ui.figma`, version `0.3.0-exp.1` (the tested version). It brings
   `com.unity.pipeline`. The Cuvara Figma menus appear after the next compile.
2. In Figma, run the Community plug-in **Unity UI Toolkit Connector**.
3. In Unity, go to **Edit > Project Settings > Pipeline > Editor**, enable **Allow Browser
   Clients**, and restart the Pipeline server.
4. Open **Window > UI Toolkit > Connector for Figma design** and select **Copy token**. Within
   60 seconds (Unity then clears the clipboard), paste it into the plug-in's **Settings** tab,
   **Pipeline token** field, and select **Connect to Unity**. The token carries the port. Copy
   it again after an Editor restart, or when the plug-in cannot reach Unity after a recompile.
5. Optional: in **Edit > Project Settings > UI Toolkit > Connector for Figma design**, select
   **Create mapping** so a mapping asset exists for the preset (see Mapping preset).
6. In Figma, select a frame and **Push selection**. The connector creates
   `Assets/Figma/<file>/` with the screen UXML/USS, `tokens.uss`, mode sheets, `images/` and
   the hidden `.figma-manifest.json`. Commit the manifest with the folder.
7. Create a profile: **Assets > Create > Cuvara > UI Toolkit > Figma Profile** (next section).
8. Open **Window > Cuvara > Figma Diagnostics** and fix what it reports.

## One profile per game or Figma file

A `FigmaProfile` asset describes how one game uses one Figma file. Create as many as you
have games or files; every tool finds all of them.

| Field | Meaning |
|---|---|
| Connector folder | The connector's import folder, e.g. `Assets/Figma/MyGame`. Read only. |
| Class prefix | The USS class prefix this game expects, e.g. `rpg` (classes like `.rpg-card`). |
| Token roles | Cuvara theme variable to Figma token, e.g. `--cuvara-color-primary` to `--brand-primary`. |
| Bridge output folder | Game-owned folder for `cuvara-theme-bridge.uss`. Must be under `Assets/` and outside every connector folder. |
| Mode sheets | Mode sheets the connector writes for this file, e.g. `light.uss`, `dark.uss`. Relative paths inside the connector folder; a rooted path or one with `..` is an error and is never read. |
| Screens namespace / folder | Where screens created from this file's UXML go. Game-owned, outside the connector folder. |

A new profile starts with the default theme roles and no Figma token mapped.

### Folder layout for several games

```
Assets/
  Figma/                     connector-owned, never edited by hand or by this package
    RpgHud/                  file 1: *.uxml, *.uss, tokens.uss, light.uss, images/, .figma-manifest.json
    ShopUi/                  file 2
  Games/
    Rpg/
      UI/FigmaProfile.asset  connector folder Assets/Figma/RpgHud, prefix "rpg"
      UI/Theme/cuvara-theme-bridge.uss
      UI/Screens/            generated views and presenters
    Shop/
      UI/FigmaProfile.asset  connector folder Assets/Figma/ShopUi, prefix "shop"
      UI/Theme/cuvara-theme-bridge.uss
```

## Class prefix

The connector emits classes only for component instances, prefixed with its **USS class
prefix** setting. Set that prefix to the game's prefix so imported classes never collide
with your own USS or with the package's `cuvara-` classes. The validator flags imported
classes that do not carry the profile's prefix.

The connector's prefix is **one project-wide setting** in its mapping asset. Two games in one
project with different prefixes cannot both be satisfied at the same time; push each file with
its own prefix active, or share one prefix.

## Mapping preset

**Tools > Cuvara > Figma > Apply Cuvara Mapping Preset** adds the Cuvara controls to the
connector's component mapping, so a Figma component named `SafeArea` (or `Safe Area`,
`safe-area`) imports as `Cuvara.UIToolkit.Utilities.SafeAreaElement`.

- It edits the **active** mapping asset, found as the connector finds it: the asset selected in
  `ProjectSettings/FigmaMappingState.asset`, otherwise the first `FigmaImportSettings` asset by
  path. If the mapping was cleared on purpose (`-`), the preset does nothing and says so. If no
  mapping asset exists, create one first: **Edit > Project Settings > UI Toolkit > Connector
  for Figma design** > **Create mapping**.
- It is **additive and idempotent**: it adds only rows whose Figma name no row matches yet. It
  never removes or changes your rows, and a second run adds nothing.
- It sets the connector's class prefix only when that prefix is **empty**, from the only
  profile that sets one (the menu) or from the profile you choose (the diagnostics window).
  A different existing prefix is reported, never overwritten.
- It edits the asset through `SerializedObject` by property name. If a connector update renames
  a property, the preset reports an error and changes nothing.
- It then checks every `uitkControl` that names a type (contains a `.`): the type must be a
  `VisualElement` in the project, and should carry `[UxmlElement]`.

## Token bridge

The bridge connects the connector's tokens to the Cuvara theme variables without copying a
value. For each mapped role it writes one alias into a game-owned file:

```css
/* Generated by Cuvara UI Toolkit from the Figma profile "Rpg". Do not edit: change the profile and regenerate. */
:root {
    --cuvara-color-primary: var(--brand-primary);
    --cuvara-color-surface: var(--background-primary);
}
```

- Generate it with **Tools > Cuvara > Figma > Regenerate Token Bridges**, the profile
  inspector's **Regenerate token bridge** button, or the diagnostics window.
- Output: `<bridge output folder>/cuvara-theme-bridge.uss`. The folder must be under `Assets/`
  and outside every connector folder, including folders no profile names but that hold a
  `.figma-manifest.json`. Otherwise nothing is written.
- Deterministic: sorted by Cuvara variable, UTF-8 without BOM, `\n` newlines, no timestamp. The
  file is rewritten only when its bytes change.
- Each Figma token is checked against the import: the manifest's token list, `tokens.uss` and
  the profile's mode sheets. An unknown token is a warning; its alias is still written, because
  the token may appear on the next push.
- The connector's `tokens.uss` is never edited. Write-back to Figma would otherwise see the edit
  as a design change.

Attach the bridge sheet after the connector's `tokens.uss` (and the active mode sheet) and
the Cuvara theme sheet, on the root your screens live under. The theme variables, their
defaults and how the host attaches theme sheets are described in [THEMING.md](THEMING.md).

Imported screens link their own mode sheet (`<Style src="light.uss"/>`), which wins over the
root's mode sheet inside the screen. To switch modes on open imported screens too, declare the
mode sheets once with `ICuvaraThemeService.RegisterModeSheets(light, dark)` and attach each
imported screen with `AttachScreen(screenRoot)` for its lifetime; `SetModeSheet` then reaches
them. See [THEMING.md](THEMING.md#imported-screens-and-their-mode-sheets).

## Diagnostics

**Window > Cuvara > Figma Diagnostics** shows:

- installed versions of `com.unity.ui.figma` and `com.unity.pipeline` against the tested range
  (connector `0.3.0-exp.1`, Pipeline `0.9.1-exp.1`), and the Unity version;
- the active mapping asset, its class prefix and row count, and unresolved mapped control types;
- per profile: connector folder and manifest found, the imported screens, the expected class
  prefix, validator results (`FigmaImportValidator`), token coverage (verified / mapped / roles)
  and bridge freshness (missing, stale, fresh);
- per profile, the **last import result**: the newest result the headless helper
  (`Tools~/FigmaImport/figma-import.py`) saved in `Library/CuvaraFigma/` for that connector
  folder (or, for a request refused before it had a folder, for that Figma file). It shows the
  summary and the connector's notes as diagnostics: the import failed, missing fonts
  (`cuvara.import.fact.MissingFonts`), images that never arrived
  (`cuvara.import.fact.MissingImages`, `cuvara.import.fact.ImagePending`), dropped features, USS
  errors, plugin input warnings. When the manifest is newer than the saved result, the window
  says the notes may be out of date: a later push from Figma replaced that import;
- saved results that match no profile, in their own section.

Only headless imports leave a saved result. A push made from the Figma plug-in directly has
none; for those, the connector window's **Last import** section is the only record of its
notes. The window re-reads saved results when it gets focus. Codes and file format:
[FIGMA-REAL-IMPORT.md](FIGMA-REAL-IMPORT.md#import-result-diagnostics).

Buttons: **Apply mapping preset**, **Regenerate token bridges**, **Re-validate**, and per
profile **Apply preset with this prefix** and **Regenerate bridge**.

The window re-validates by itself after a push. An `AssetPostprocessor` watches `.uxml` and
`.uss` imports, moves and deletions under every profile's connector folder. Unity does not
import dot-files, so the `.figma-manifest.json` itself never triggers an import; the sheets
a push rewrites do. The work is debounced through `EditorApplication.delayCall`, it only reads,
and it writes nothing into the connector folder, so it cannot trigger itself. Each validated
profile logs one Console line; a line with errors or warnings is a warning, never an error.

**Tools > Cuvara > Figma > Validate Profiles** runs the same validation on demand.

## Screens from imported UXML

**Assets > Cuvara > Create Screen** has a **Source** option:

- **New UXML** (default): unchanged. Writes the C#, a new `.uxml`, a `.uss` (not for items)
  and a test skeleton.
- **Existing UXML (e.g. Figma import)**: pick the imported `.uxml`. The wizard writes the C#
  (view, view interface, presenter, and model when chosen) and the test skeleton only. It
  writes **no** `.uxml` and **no** `.uss`. The view declares `public const string AssetKey`,
  defaulting to the UXML file name; register it with
  `builder.RegisterScreen<ShopPresenter, ShopView>(ShopView.AssetKey)` (or `RegisterPopup`) and
  make your `IVisualTreeAssetLoader` resolve that key to the imported UXML.

The generated view queries no element: a Figma import names none (see the limitations).
Choose a game-owned output folder; the wizard refuses a folder inside a connector import
folder, for either source.

## Supported and unsupported Figma features

> **Designer rule: bind Figma variables on components, not on plain frames.** On Unity 6000.3,
> UI Toolkit cannot clone a UXML whose inline `style="..."` contains `var(...)`: `CloneTree`
> throws a `NullReferenceException` in `StyleVariableResolver.ResolveVariable` when the view is
> created (verified on 6000.3.9f1; Unity's documentation lists variables as unsupported in
> inline styles). Connector `0.3.0-exp.1` writes a bound property of a non-component layer as
> inline `var(--token)`. Put variable-bound styling on Figma **components**: the component's
> values land in class rules in its `.uss`, where `var()` works. Instances carry an inline delta
> for their overrides, so do not bind a variable as a per-instance override either. The
> inline-var validator rule (`FIG023`) reports every such `style` attribute as an error before
> the screen is opened.
>
> Verified with a real import (see [FIGMA-REAL-IMPORT.md](FIGMA-REAL-IMPORT.md#recorded-evidence-2026-10-31)):
> only the **component's own root** lands in a class rule. Layers *inside* a component are plain
> layers again. For text colours and text styles, make each text role a single-text component
> named `<Role>/Label` (for example `Title/Label`, `Body/Label`): the connector maps it to
> `ui:Label` by name and folds the text style, including its bound colour, into the component's
> class. Frames such as cards become components whose root carries the bound fill, radius and
> padding. The screen root itself is a plain frame, so its background stays a literal (or comes
> from the game's theme).

What survives an import with connector `0.3.0-exp.1` on Unity 6000.3. Sources: the connector's
own documentation and source (audit §2), and its troubleshooting page.

| Figma feature | On Unity 6000.3 | Notes |
|---|---|---|
| Frames | supported | one `<Screen>.uxml` + `.uss` per pushed frame |
| Auto layout (direction, padding, alignment, hug/fill) | supported | becomes flexbox |
| Auto layout gap | **dropped** | USS `gap` needs Unity 6000.7.0a4. Set margins in a game-owned sheet. |
| Negative auto layout spacing | **dropped** | USS has no negative `gap`. Use absolute positioning or negative margins. |
| Frames without auto layout, `ABSOLUTE` children | supported | `position: absolute; left; top` (`FIG032` counts them) |
| Grid layout | **degraded** | needs Unity 6000.7.0b3; on 6000.3 cells keep fixed sizes and positions |
| Solid fills, strokes, corner radius, opacity | supported | inline style, or a class on component instances |
| Gradients | **degraded** | USS gradients need Unity 6000.7.0b5; on 6000.3 they import as images |
| Image fills | supported as PNG | plain textures, not sprites; Fill = `scale-and-crop`, Fit = `scale-to-fit`, Tile = repeat |
| Vectors and icons | supported as PNG | rasterised by the plug-in; no vector graphics, no per-instance colour overrides |
| Drop shadow on text | supported | `text-shadow` |
| Drop shadow on frames and shapes | **dropped** | USS has no `box-shadow` |
| Layer blur | supported | `filter: blur()` |
| Drop shadow on vectors | **dropped** | `filter: drop-shadow()` needs Unity 6000.6 |
| Background blur | **dropped** | `backdrop-filter` needs Unity 6000.6.0a6 |
| Blend modes | **dropped** | USS has none; flatten the layer in Figma |
| Text, fonts | supported | through the mapping asset's font map, then by file name; a missing font falls back to the default (`cuvara.import.fact.MissingFonts` in a saved headless result) |
| Components | supported | a template `.uxml`/`.uss` per used component; instances carry a class plus an inline delta |
| Component to control (Button, Toggle, Slider, ...) | supported | by name, by plug-in tag, or by a mapping row (the Cuvara preset adds `SafeArea`) |
| Variants | partial | variant styles only for components with a single text layer |
| Component properties: TEXT, BOOLEAN | supported | bindable to UXML attributes through mapping rows |
| Component properties: instance swap | **dropped** | not passed to mapping rows |
| Variables: COLOR, FLOAT | supported | `tokens.uss` and one `<mode>.uss` per mode; bound properties become `var(--token)` |
| Variables bound on a component | supported | `var(--token)` in the component's class rules |
| Variables bound on a plain frame or layer (not a component) | **breaks the screen** | written as inline `var(--token)`, which UI Toolkit 6000.3 cannot clone (`NullReferenceException` in `StyleVariableResolver.ResolveVariable`); the inline-var validator rule (`FIG023`) reports it |
| Variables: STRING, BOOLEAN | **dropped** | never written to a token sheet |
| Multiple variable modes | supported | needs a paid Figma plan (Free has one mode per collection) |
| Unbound properties | literal | hard-coded values; the theme cannot reach them (`FIG020`) |
| Layer names | **not emitted** | no `name` attributes; only `text-<i>` inside templates |
| Element styling | inline | non-component elements carry their whole style in `style="..."` (`FIG030`) |
| USS class prefix | one per project | a single setting in the active mapping asset |
| Write-back to Figma | partial | style, layout, interaction-state and token edits; no structural edits |

## Limitations

- **No element names from Figma (gap G4, deferred).** The connector never turns layer names into
  `name` attributes, so `Require<T>("name")` and the typed UXML codegen have nothing to bind to
  in an imported UXML. Bindings keyed on the stable Figma node id are planned once a real import
  confirms the runtime element paths. Until then, query by structure or add names in a
  game-owned UXML that instantiates the imported one.
- **Inline styles.** Elements that are not component instances carry their whole style inline
  (`style="..."`); instances carry a class plus an inline delta. Theme variables reach only the
  values that were bound to Figma variables (`var(--token)`); unbound values stay literals. The
  validator reports inline-style and unbound-literal counts.
- **No `var()` in inline styles.** A variable bound on a plain layer becomes inline
  `var(--token)`, and UI Toolkit 6000.3 throws while cloning that UXML. Bind variables on
  components only (see the designer rule above).
- **Moving an import folder breaks image URLs.** The connector writes absolute
  `project://database/Assets/...?guid=...` image URLs. After you move `Assets/Figma/<file>/`, each
  one still names the old path, and the validator, which checks the path only, reports every one
  as `FIG040`. The connector's documentation says the images still load and Unity may warn
  until you push that file again; this package has not verified the `guid=` fallback. Push the
  file again after a move so the connector rewrites the URLs.
- **Images are PNG.** The plugin rasterises vectors and image fills; they import as plain
  textures, not sprites and not vector graphics.
- **Features of later Unity versions.** `gap` (6000.7.0a4), grid layouts (6000.7.0b3), USS
  gradients (6000.7.0b5), `backdrop-filter` (6000.6.0a6) and `filter: drop-shadow()` (6000.6)
  are not imported on 6000.3. Grids keep fixed cell sizes and positions, and gradients import as
  images. `filter: blur()` (layer blur) works on 6000.3. Full list: Supported and unsupported
  Figma features, above.
- **Experimental connector.** `0.x` releases may change the manifest or the mapping asset. The
  diagnostics window shows when the installed version is not the tested one; the preset and the
  validator report renamed fields as errors instead of failing.
- **One class prefix per project** (see Class prefix).

## CI

The `figma-connector` job in `.github/workflows/ci.yml` bootstraps the same test project as
the `test` job plus `com.unity.ui.figma` `0.3.0-exp.1`, so the gated assembly compiles and its
EditMode tests run. It fails on zero executed tests, and also when no
`Cuvara.UIToolkit.Editor.Figma.Tests` case ran, which means the gate did not open.
