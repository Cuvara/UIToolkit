# Figma Import

An end-to-end example of a game adopting Unity's **UI Toolkit Connector for Figma**
(`com.unity.ui.figma`) with this package: an imported screen driven by a Model, View and
Presenter, the Cuvara theme fed by the Figma tokens, a light/dark mode switch, and the
package's own dialog and toasts following the same tokens.

Import it, open `FigmaImportSample.unity`, press Play. Click **Buy** for the confirm dialog and
a toast, **Dark mode** to switch modes, **Show toast** for an info toast.

**The connector is not needed to run it.** `ConnectorOutput/` is a hand-made stand-in for what
the connector writes (see `ConnectorOutput/README.md`). The sample's code uses only the
package's runtime, UniTask and VContainer, so it compiles and runs in a project without the
connector.

## What is in the sample

| Path | Owner in a real game | What it is |
|---|---|---|
| `ConnectorOutput/` | the connector | stand-in for `Assets/Figma/<file>/`: `ShopCard.uxml`/`.uss`, the `PriceTag` component, `tokens.uss`, `light.uss`, `dark.uss`, `images/`, `.figma-manifest.json` |
| `ShopFigmaProfile.asset` | the game | the `FigmaProfile` for this file: class prefix `shop`, 13 of the 20 theme roles mapped to Figma tokens, mode sheets, output folders |
| `Theme/cuvara-theme-bridge.uss` | the game, generated | `--cuvara-*: var(--figma-token);` aliases, exactly as **Regenerate Token Bridges** writes them for the profile |
| `ShopCardScreen.cs` | the game | Model, View interface, View and Presenter, started from **Create Screen > Existing UXML** |
| `FigmaModeSwitcher.cs` | the game | switches light/dark for the document root and for open imported screens |
| `FigmaImportSampleScope.cs` | the game | VContainer composition: package, loader, screen, theme sheets |
| `FigmaImportSampleBootstrap.cs` | the game | attaches the theme sheets, adds the toolbar, opens ShopCard |
| `FigmaImportSample.unity`, `FigmaImportSamplePanel.asset`, `FigmaImportSampleTheme.tss` | the game | scene with a `RootUIDocument` and the scope |

`ShopFigmaProfile.asset` is an Editor asset of `Cuvara.UIToolkit.Editor.Figma`, which compiles
only when the connector is installed. Without the connector the asset shows "The associated
script can not be loaded". That is expected and harmless: the scene does not use it. Its
folders assume the default import location
`Assets/Samples/Cuvara UI Toolkit/<version>/Figma Import/`; if yours differs, fix the three
folder fields in the inspector.

## How the pieces connect

```
Figma file ──(plugin push)──► connector ──► Assets/Figma/<file>/   (connector-owned, read only)
                                              ShopCard.uxml ──► IVisualTreeAssetLoader ──► ShopCardView
                                              tokens.uss ─┐
                                   cuvara-theme-bridge.uss ├──► ICuvaraThemeService overrides ──► document root
                                     light.uss / dark.uss ─┘    mode sheet
```

- **Theme.** `FigmaImportSampleBootstrap` adds `tokens.uss` then `cuvara-theme-bridge.uss` as
  overrides and `light.uss` as the mode sheet. `tokens.uss` must be at the document root as
  well as on the screen, because the dialog, the toasts and the scrim are in the overlay layer,
  outside the screen's subtree. The same is possible without code: set **Theme Overrides** and
  **Theme Mode Sheet** on `RootUIDocument`.
- **Modes.** `ICuvaraThemeService.SetModeSheet` re-themes the built-in UI. An imported screen
  links its own `tokens.uss` and `light.uss`, and those re-declare the tokens on the screen's
  root, below the document root. `ICuvaraThemeService.AttachScreen` keeps such a screen on the
  current mode; `FigmaModeSwitcher` registers the two mode sheets and wraps it. A presenter
  opts in with `subs.Add(modes.Track(this.View.Root))`, so the screen is released when it closes.
- **Elements.** The connector emits no `name` attributes except `text-<i>` inside templates.
  `ShopCardView` finds the item name, the description and the Buy button by their component
  classes (`shop-card-title`, `shop-card-description`, `shop-primary-button`), and the price by
  `text-0` inside the `shop-price-tag` instance. Each lookup throws a message that names what
  changed in Figma.
- **Tokens reach elements through class rules only.** Every `var(--token)` in the stand-in is
  in a component class rule in `ShopCard.uss` or `PriceTag.uss`. Inline `style=` attributes
  hold literal layout only (`width`, `height`, flex, the image). See the designer rule below.

## Designer rule: bind tokens on components, not on plain layers

UI Toolkit on Unity 6000.3 does not support `var()` inside an inline `style=` attribute.
`VisualTreeAsset.CloneTree` fails on it with a `NullReferenceException` in
`StyleVariableResolver.ResolveVariable`, so the screen cannot be opened at all.

The connector writes a plain (non-component) layer's whole style inline, including
`var(--token)` for every value bound to a Figma variable. A component instance gets a class
rule in the screen's `.uss` instead, and its bound values go there. So, in Figma:

- **Bind variables only on component instances** (colours, radii, spacing, text styles).
  Turn a plain frame or text layer that needs a token into a component first.
- **Leave plain layers unbound.** Their values stay literals, which is valid inline, but they
  do not follow the theme or the light/dark mode.

The validator's inline-`var()` rule reports a screen that breaks this rule. Fix the design in
Figma and push again; never edit the imported UXML.

## Adopting it in your game

1. **Install.** Package Manager > **Add package by name** > `com.unity.ui.figma`, version
   `0.3.0-exp.1` (the tested version; it brings `com.unity.pipeline`). In Figma, install the
   Community plugin **Unity UI Toolkit Connector** (Dev or Full seat). Connect it: connector
   window > **Allow Browser Clients** > **Copy token**, paste it into the plugin.
2. **Choose a class prefix** for the game, for example `shop` or `rpg`. Connector classes then
   never collide with your USS or with the package's `cuvara-` classes.
3. **Push one frame** from Figma. The connector creates `Assets/Figma/<file>/`.
4. **Create a profile:** **Assets > Create > Cuvara > UI Toolkit > Figma Profile**. Set the
   connector folder (`Assets/Figma/<file>`), the class prefix, the mode sheets
   (`light.uss`, `dark.uss`), and game-owned folders for the bridge and the screens, outside
   the connector folder. Map the theme roles you want to Figma tokens; leave the rest empty to
   keep the Cuvara defaults. `ShopFigmaProfile.asset` is a filled-in example.
5. **Apply the mapping preset:** **Tools > Cuvara > Figma > Apply Cuvara Mapping Preset**, or
   **Apply preset with this prefix** in the diagnostics window. It sets the connector's empty
   class prefix from the profile and adds the Cuvara control rows. Push again so the classes
   carry the prefix.
6. **Regenerate the bridge:** **Tools > Cuvara > Figma > Regenerate Token Bridges**. It writes
   `<bridge folder>/cuvara-theme-bridge.uss`. Regenerate after a token is renamed in Figma;
   a value change needs nothing.
7. **Run diagnostics:** **Window > Cuvara > Figma Diagnostics**. Fix every error: versions,
   mapping asset, prefix, validator findings, token coverage, bridge freshness. In CI, run
   `UxmlCodegenCli validate Assets/Figma/<file> --prefix <prefix>` on the committed import.
8. **Create the screen:** **Assets > Cuvara > Create Screen**, Source **Existing UXML (e.g.
   Figma import)**, pick the imported `.uxml`, output to a game-owned folder. Register it with
   the line the generated presenter carries,
   `builder.RegisterScreen<XPresenter, XView>(XView.AssetKey)`, and make your
   `IVisualTreeAssetLoader` resolve `XView.AssetKey` to the imported UXML. Then add the queries
   and bindings by hand, as `ShopCardScreen.cs` shows.
9. **Attach the theme:** overrides `tokens.uss`, then `cuvara-theme-bridge.uss`; mode sheet
   `light.uss` (code as in `FigmaImportSampleBootstrap`, or the `RootUIDocument` fields).
10. **Switch modes** with `ICuvaraThemeService.SetModeSheet`. Declare the mode sheets with
    `RegisterModeSheets` and attach each open imported screen with `AttachScreen`, as
    `FigmaModeSwitcher` does, so the screen's own linked mode sheet follows the switch.

Never edit files in `Assets/Figma/<file>/`. The next push rewrites them, and the connector's
write-back would see your edits as design changes.

## Limitations

- **No element names from Figma (gap G4, deferred).** Layer names do not reach the UXML, so the
  typed UXML codegen and `Require<T>("name")` have nothing to bind to. Node-id bindings wait
  until a real import confirms the runtime element paths. Until then, query by component class
  or by template `text-<i>` names, as `ShopCardView` does, by position as a last resort, or
  wrap the imported UXML in a game-owned UXML that names its own containers.
- **No `var()` in inline styles.** Unity 6000.3 cannot clone a UXML whose inline `style=` holds
  `var()` (see the designer rule). Plain layers therefore cannot follow the theme; only
  component instances can.
- **Inline styles.** Plain layers carry their whole style inline. Instances carry a class
  rule plus an inline delta.
- **PNG images.** The plugin rasterises vectors and image fills. They import as plain
  textures, not sprites and not vector graphics.
- **Experimental connector.** `0.x` releases may change the files. The diagnostics window shows
  when the installed version is not the tested one.
- **One class prefix per project.** The connector's prefix is a single project-wide setting.

## Missing real-world integration requirements

Found while building this sample; none is solved by the package yet.

- **Tokens on plain layers break the screen.** The connector writes `var()` into inline styles
  for plain layers, which Unity 6000.3 cannot clone. Until the connector or Unity changes, the
  only safe design is tokens on components (see the designer rule).
- **The profile stores project paths.** A profile cannot point at a folder whose path depends
  on a version (a sample import), and moving the game's folders breaks it silently until
  diagnostics run.
- **No tested real import.** The fixture mirrors the connector's source, not a captured push.
  The runtime path of `<ui:Instance>` children, image `url()` resolution after a folder move,
  and font mapping are unverified until a real import is recorded.
- **Image URLs are absolute.** The connector writes `project://database/Assets/...` URLs. Moving
  an import folder leaves every image reference pointing at the old path, and the validator
  reports each one as `FIG040`. Whether Unity resolves them through the `guid=` query is not
  verified. This is also why the stand-in uses a relative URL.
- **No font mapping example.** Fonts resolve through the connector's font map; a game must ship
  its fonts and map them, and the validator cannot yet report missing fonts.
- **No headless verification in CI.** A game's CI can validate committed output, but not that the
  committed output matches the current Figma file.
