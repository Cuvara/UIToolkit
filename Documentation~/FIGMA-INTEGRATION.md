# Figma integration

How to use Unity's **UI Toolkit Connector for Figma** (`com.unity.ui.figma`) with this
package. The rule: **the connector imports, this package integrates.** The connector turns
Figma frames into UXML, USS and token sheets. This package adds a per-game profile, a token
bridge to the Cuvara theme, a component mapping preset, diagnostics, and screen scaffolding
over the imported UXML. It never converts Figma data itself, and it never edits a file the
connector wrote.

The background, and the gap list the features below come from, is the connector audit
(gaps G2, G3, G6, G7, G9).

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

1. Install the connector: Package Manager > **Add package by name** > `com.unity.ui.figma`,
   version `0.3.0-exp.1` (the tested version). It brings `com.unity.pipeline`.
2. In Figma, install the Community plugin **Unity UI Toolkit Connector** (Dev or Full seat).
3. In Unity, open the connector window, enable **Allow Browser Clients**, and use **Copy
   token** to paste the token into the Figma plugin. The plugin then pushes frames into the
   project.
4. Push a frame once. The connector creates `Assets/Figma/<file>/` with the screen UXML/USS,
   `tokens.uss`, mode sheets, `images/` and the hidden `.figma-manifest.json`.
5. Create a profile: **Assets > Create > Cuvara > UI Toolkit > Figma Profile** (next section).
6. Open **Window > Cuvara > Figma Diagnostics** and fix what it reports.

## One profile per game or Figma file

A `FigmaProfile` asset describes how one game uses one Figma file. Create as many as you
have games or files; every tool finds all of them.

| Field | Meaning |
|---|---|
| Connector folder | The connector's import folder, e.g. `Assets/Figma/MyGame`. Read only. |
| Class prefix | The USS class prefix this game expects, e.g. `rpg` (classes like `.rpg-card`). |
| Token roles | Cuvara theme variable to Figma token, e.g. `--cuvara-color-primary` to `--brand-primary`. |
| Bridge output folder | Game-owned folder for `cuvara-theme-bridge.uss`. Must be under `Assets/` and outside every connector folder. |
| Mode sheets | Mode sheets the connector writes for this file, e.g. `light.uss`, `dark.uss`. |
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
  mapping asset exists, create one on the connector's Project Settings page first.
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

## Diagnostics

**Window > Cuvara > Figma Diagnostics** shows:

- installed versions of `com.unity.ui.figma` and `com.unity.pipeline` against the tested range
  (connector `0.3.0-exp.1`, Pipeline `0.9.1-exp.1`), and the Unity version;
- the active mapping asset, its class prefix and row count, and unresolved mapped control types;
- per profile: connector folder and manifest found, the imported screens, the expected class
  prefix, validator results (`FigmaImportValidator`), token coverage (verified / mapped / roles)
  and bridge freshness (missing, stale, fresh).

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
- **Images are PNG.** The plugin rasterises vectors and image fills; they import as plain
  textures, not sprites and not vector graphics.
- **Unity 6000.7-only features.** `gap`, `filter` (6000.6), grid layouts and USS gradients need
  Unity 6000.6/6000.7. On 6000.3, grids keep fixed cell sizes and positions, and gradients
  import as images.
- **Experimental connector.** `0.x` releases may change the manifest or the mapping asset. The
  diagnostics window shows when the installed version is not the tested one; the preset and the
  validator report renamed fields as errors instead of failing.
- **One class prefix per project** (see Class prefix).

## CI

The `figma-connector` job in `.github/workflows/ci.yml` bootstraps the same test project as
the `test` job plus `com.unity.ui.figma` `0.3.0-exp.1`, so the gated assembly compiles and its
EditMode tests run. It fails on zero executed tests, and also when no
`Cuvara.UIToolkit.Editor.Figma.Tests` case ran, which means the gate did not open.
