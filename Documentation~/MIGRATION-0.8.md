# Migrating from 0.7.2 to 0.8.0

This page lists what changes for a project that moves `com.cuvara.uitoolkit` from `0.7.2` to
`0.8.0`. Most projects need no code change. Read the theme section if your game styles the
built-in `ConfirmDialog`, `ToastService` or the navigator scrim, or if it attaches its own
stylesheets to the `RootUIDocument` root.

## Summary

| Change | Action needed |
|---|---|
| Built-in UI is styled by `cuvara-*` classes, not inline styles | none, unless you restyle the built-in UI (see 1) |
| `RootUIDocument` has three new serialized fields; `applyTheme` is on by default | none, unless you set `--cuvara-*` from your own root sheet (see 2) |
| `RegisterUIToolkit()` also registers `ICuvaraThemeService` | none, unless you register that interface yourself (see 2) |
| Screen Creator refuses output folders inside a Figma import folder | choose a game-owned folder (see 3) |
| `UxmlCodegenCli` has a new `validate` verb | none; the drift check is unchanged (see 4) |
| New assemblies `Cuvara.UIToolkit.Editor.Figma` (gated) and its tests | none; they compile only with `com.unity.ui.figma` (see 5) |

**No breaking public API changes.** This was checked with
`git diff origin/main...HEAD -- Runtime Editor` on the release branch. No `public` or
`protected` member was removed or changed. The old `ScreenCreatorTemplates.SelectScriptTemplate(ScreenType, bool)`
and `ScreenCreatorTemplates.Substitute(string, string, string)` overloads are kept next to the
new ones. All public API changes are additions (listed at the end of this page).

## 1. Theme contract for the built-in UI

**What changed.** In 0.7.2 `ConfirmDialog`, `ToastService` and the `ScreenNavigator` scrim set
colours, padding, radii and font sizes as inline styles in C#. In 0.8.0 they add `cuvara-*` USS
classes, and `Runtime/Theme/Resources/CuvaraTheme.uss` styles those classes from
`--cuvara-*` variables. The variable defaults equal the old inline values, so a project that
does nothing sees the same UI. Token table and class list: [THEMING.md](THEMING.md).

**What stays inline.** The overlay and scrim stretch (`position: absolute`, zero insets), the
toast container's `position: absolute`, and every `pickingMode`. A theme cannot make a modal
stop covering the screen.

**How the stylesheet reaches the elements.** When a component builds its top element, it looks
for an ancestor that carries `cuvara-theme-root` or `cuvara-theme-local`:

1. Found: the element inherits the theme from that ancestor.
2. Not found: the component attaches the default sheet to its own top element and marks it
   `cuvara-theme-local`.
3. The sheet cannot be loaded (stripped, or not imported): the component applies the exact
   0.7.2 inline styles. This is the fallback, so rendering never regresses to an unstyled dialog.

**Consequences.**

- Inline styles always won over USS. Class rules do not. A host USS rule that matches these
  elements (for example a type selector on `Button` or `Label` in a sheet above the overlay
  layer) can now change the dialog or toast where it could not before. Check the built-in UI
  visually after the upgrade if your game has broad type selectors.
- The default sheet is loaded with `Resources.Load<StyleSheet>("CuvaraTheme")`. Do not give a
  `StyleSheet` in your own `Resources` folders the name `CuvaraTheme`, or the package may load
  yours instead.
- The sheet lives in a package `Resources` folder, so it is included in player builds.

**How to restyle.** Set the variables, do not edit `CuvaraTheme.uss`:

```css
/* Assets/UI/Theme/cuvara-overrides.uss */
:root {
    --cuvara-color-primary: rgb(255, 140, 0);
    --cuvara-radius-md: 12px;
}
```

Add that sheet to `RootUIDocument.themeOverrides`, or call `ICuvaraThemeService.AddOverride`.
Write rules against the classes (constants on `Cuvara.UIToolkit.Theme.CuvaraTheme`) only when no
variable covers what you need. The class names are public API from 0.8.0.

## 2. `RootUIDocument` theme fields

`RootUIDocument` has three new serialized fields:

| Field | Default | Meaning |
|---|---|---|
| `applyTheme` | `true` | apply the theme service to `rootVisualElement` when the layers resolve |
| `themeOverrides` | empty | per-game sheets, attached after the base theme, in order |
| `themeModeSheet` | none | initial mode sheet (light, dark, ...) |

Existing scenes and prefabs have no serialized value for these fields, so Unity uses the
defaults: **the theme is applied** on the first load after the upgrade. When applied, the
service:

- adds `cuvara-theme-root` to `rootVisualElement`;
- appends the base theme sheet, then the overrides, then the mode sheet, as the **last** sheets
  on `rootVisualElement`;
- removes the local sheet copies from built-in components already below the root.

The base sheet declares only `--cuvara-*` variables on `:root` and rules for `cuvara-*` classes.
It does not restyle your own elements.

**Watch for this case.** If your game attached its own sheet to `rootVisualElement` that sets
`--cuvara-*` variables, the base sheet now comes after it and wins at equal specificity. Move
that sheet into `themeOverrides` (or call `Theme.AddOverride`), which always comes after the base.

**Opting out.** Turn `applyTheme` off and call `rootUIDocument.Theme.Apply(root)` yourself on
the root you want themed. The built-in components still style themselves through their local
sheet copy, so turning the field off does not leave them unstyled.

**Dependency injection.** `RegisterUIToolkit()` now registers `ICuvaraThemeService` as a
singleton that resolves to `RootUIDocument.Theme`. A presenter can inject it to switch modes
with `SetModeSheet`. The interface is new in 0.8.0, so a 0.7.2 project cannot already register
it.

## 3. Screen Creator and Figma import folders

**Assets > Cuvara > Create Screen** has a new **Source** option. **New UXML** is the default
and generates the same files as 0.7.2. **Existing UXML (e.g. Figma import)** writes only the C#
and the test skeleton for a UXML you pick, and the view declares `public const string AssetKey`.

For **both** sources, the wizard now refuses an output folder that is inside a Figma connector
import folder: that folder, or any parent up to the project root, holds a
`.figma-manifest.json`. The connector rewrites that folder on every push. Choose a game-owned
folder instead. Details: [FIGMA-INTEGRATION.md](FIGMA-INTEGRATION.md#screens-from-imported-uxml).

## 4. `UxmlCodegenCli` `validate` verb

`Tools~/UxmlCodegenCli` has a new first argument, `validate`, which runs the Figma import
validator ([FIGMA-VALIDATION.md](FIGMA-VALIDATION.md)). Any other first argument is a
drift-check root, as in 0.7.2, with the same exit codes (0 in sync, 1 drift, 2 a root does not
exist). The only input that changes meaning is a drift-check root literally named `validate`;
pass it as `./validate` instead.

The project also compiles `Editor/FigmaCore/*.cs` now. Those files are Unity-free, like
`Editor/Codegen/Core/`.

## 5. New assemblies

| Assembly | Compiles when | Contains |
|---|---|---|
| `Cuvara.UIToolkit` (existing) | always | new `Runtime/Theme/` (namespace `Cuvara.UIToolkit.Theme`) |
| `Cuvara.UIToolkit.Editor` (existing) | always (Editor) | new `Editor/FigmaCore/` (namespace `Cuvara.UIToolkit.FigmaCore`) and `ScreenCreatorPlan` |
| `Cuvara.UIToolkit.Editor.Figma` (new) | Editor, and `com.unity.ui.figma` `0.3.0-exp.1` or later is installed | profiles, token bridge, mapping preset, diagnostics window |
| `Cuvara.UIToolkit.Editor.Figma.Tests` (new) | tests enabled, and the same connector condition | tests of the assembly above |

`Cuvara.UIToolkit.Editor.Figma` is gated: its `versionDefines` sets `CUVARA_UITOOLKIT_FIGMA`
when the connector is installed, and its `defineConstraints` requires that define. Without the
connector the assembly is skipped and nothing else changes. No runtime assembly references
Figma or the connector.

## Public API added in 0.8.0

Runtime (`Cuvara.UIToolkit`):

- `Cuvara.UIToolkit.Theme.CuvaraTheme`: `ResourcesPath`, the class-name constants
  (`ThemeRootUssClassName`, `LocalThemeUssClassName`, `ScrimUssClassName`,
  `ScrimDimUssClassName`, `ConfirmDialog*UssClassName`, `Toast*UssClassName`),
  `DefaultStyleSheet`, `IsThemed(VisualElement)`.
- `Cuvara.UIToolkit.Theme.ICuvaraThemeService`: `Apply`, `AddOverride`, `SetModeSheet`.
- `Cuvara.UIToolkit.Theme.CuvaraThemeService`: two constructors, `BaseSheet`, `Overrides`,
  `ModeSheet`, and the interface members.
- `RootUIDocument.Theme`.

Editor (`Cuvara.UIToolkit.Editor`):

- `Cuvara.UIToolkit.FigmaCore`: `FigmaImportValidator`, `FigmaValidationOptions`,
  `FigmaDiagnostic`, `FigmaDiagnosticSeverity`, `FigmaDiagnostics`, `FigmaManifestReader`,
  `FigmaManifestInfo`, `FigmaManifestScreen`, and the readers `FigmaJson`, `FigmaUss`,
  `UssSheet`, `UssRule`, `UssDeclaration`.
- `ScreenSource`, `ScreenCreatorPlan`, `ScreenCreatorRequest`, `ScreenCreatorFile`, and on
  `ScreenCreatorTemplates`: `PH_ASSET_KEY`,
  `PH_UXML_PATH`, `SelectScriptTemplate(ScreenType, bool, ScreenSource)`,
  `Substitute(string, string, string, string, string)`, `DefaultAssetKey(string)`.

Editor, gated (`Cuvara.UIToolkit.Editor.Figma`): `FigmaProfile`, `FigmaTokenRole`,
`FigmaTokenBridge`, `FigmaTokenBridgeResult`, `FigmaBridgeFreshness`, `FigmaMappingPreset`,
`FigmaControlMapRow`, `FigmaMappingSelection`, `FigmaMappingPresetCommand`,
`FigmaMappingAssetInfo`, `FigmaPaths`, `FigmaProfileValidator`, `FigmaProfileReport` and
`FigmaDiagnosticsWindow`. This assembly
follows an experimental connector; treat its API as unstable.
