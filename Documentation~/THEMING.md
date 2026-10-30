# Theming: the Cuvara theme contract

The package's own UI is `ConfirmDialog`, `ToastService` and the navigator's tap-outside scrim.
It is styled by `cuvara-*` USS classes. Those classes read semantic `--cuvara-*` custom
properties. A game re-skins the built-in UI by setting the properties. It does not restyle
the classes, and it does not touch C#.

Upgrading from 0.7.2, including what `applyTheme` (on by default) does to existing scenes:
[MIGRATION-0.8.md](MIGRATION-0.8.md).

This is gap G1 of the Figma connector audit. It has no Figma dependency: the runtime knows only
USS and `StyleSheet` assets. The Figma side (token bridge, G2) produces a sheet that plugs into
this contract.

## Rendering is unchanged by default

Every default equals the inline value the package used before this contract existed. The
sources were `ConfirmDialog.cs`, `ToastService.cs` (including `GetColor`) and the
`ScreenNavigator` scrim. A host that does nothing gets the same visuals.

Colour channels are written as 0-255 numbers derived from the old 0-1 floats, not rounded
(`0.15 * 255 = 38.25`). The test `UssColourDefault_EqualsPreThemeInlineValue`
(`Tests/Runtime/Theme/CuvaraThemeTests.cs`) reads the `.uss` text and checks every colour
against the old float.

## Tokens

Declared on `:root` in `Runtime/Theme/Resources/CuvaraTheme.uss`.

| Token | Role | Default | Used by |
|---|---|---|---|
| `--cuvara-color-surface` | background of a component panel | `rgba(38.25, 38.25, 38.25, 1)` (0.15 grey) | dialog panel |
| `--cuvara-color-surface-raised` | background of a floating element above content | `rgba(63.75, 63.75, 76.5, 0.95)` | info toast (via `--cuvara-color-toast-info`) |
| `--cuvara-color-on-surface` | text on a surface | `rgba(255, 255, 255, 1)` | dialog message, toast text (via `--cuvara-color-on-toast`) |
| `--cuvara-color-primary` | primary action | `rgba(51, 127.5, 229.5, 1)` (0.2, 0.5, 0.9) | dialog confirm button |
| `--cuvara-color-on-primary` | text on primary | `rgba(255, 255, 255, 1)` | dialog confirm button |
| `--cuvara-color-scrim` | dimming behind a modal | `rgba(0, 0, 0, 0.5)` | dialog overlay, navigator scrim with `DimsBelow` |
| `--cuvara-color-toast-info` | info toast | `var(--cuvara-color-surface-raised)` | `ToastType.Info` |
| `--cuvara-color-toast-success` | success toast | `rgba(51, 178.5, 76.5, 0.95)` | `ToastType.Success` |
| `--cuvara-color-toast-warning` | warning toast | `rgba(229.5, 178.5, 25.5, 0.95)` | `ToastType.Warning` |
| `--cuvara-color-toast-error` | error toast | `rgba(229.5, 63.75, 51, 0.95)` | `ToastType.Error` |
| `--cuvara-color-on-toast` | toast text | `var(--cuvara-color-on-surface)` | toast label |
| `--cuvara-space-xs` | spacing | `8px` | gap between toasts |
| `--cuvara-space-sm` | spacing | `10px` | toast vertical padding, gap between dialog buttons |
| `--cuvara-space-md` | spacing | `15px` | toast horizontal padding |
| `--cuvara-space-lg` | spacing | `20px` | dialog vertical padding, message bottom margin, toast stack offset |
| `--cuvara-space-xl` | spacing | `30px` | dialog horizontal padding |
| `--cuvara-radius-sm` | corner radius | `6px` | toast |
| `--cuvara-radius-md` | corner radius | `8px` | dialog panel |
| `--cuvara-font-size-sm` | font size | `14px` | toast text |
| `--cuvara-font-size-md` | font size | `16px` | dialog message |

Every component rule also repeats its default as a `var()` fallback, for example
`var(--cuvara-color-surface, rgba(38.25, 38.25, 38.25, 1))`. A sheet that removes a variable
therefore still gets the default.

## Classes

The class names are public API (constants on `CuvaraTheme`). Write host rules against them only
when a variable is not enough.

| Class | Element |
|---|---|
| `cuvara-confirm-dialog` | dialog's full-screen overlay |
| `cuvara-confirm-dialog__panel` | dialog box |
| `cuvara-confirm-dialog__message` | message label |
| `cuvara-confirm-dialog__buttons` | button row |
| `cuvara-confirm-dialog__confirm`, `cuvara-confirm-dialog__cancel` | the two buttons |
| `cuvara-toast-container` | toast stack (element name `toast-container` is unchanged) |
| `cuvara-toast`, `cuvara-toast--info` / `--success` / `--warning` / `--error` | one toast and its type |
| `cuvara-toast__label` | toast text |
| `cuvara-scrim`, `cuvara-scrim--dim` | navigator scrim (element name `cuvara-scrim` is unchanged); `--dim` only with `DimsBelow` |
| `cuvara-theme-root` | set by the theme service on a root it themed |
| `cuvara-theme-local` | set on a component element that carries its own copy of the default sheet |

Some styles stay inline because they are functional, not visual:

- the overlay and scrim stretch (`position: absolute` and zero insets);
- the toast container's `position: absolute`;
- every `pickingMode`.

A theme must not be able to make a modal stop covering the screen.

## How the sheet reaches the elements

**The mechanism.** The default sheet lives in a package `Resources` folder.
`CuvaraTheme.DefaultStyleSheet` loads it once with `Resources.Load<StyleSheet>("CuvaraTheme")`,
so do not name a `StyleSheet` of your own `CuvaraTheme` in a `Resources` folder. When a component builds its
top element (dialog overlay, toast container, dimmed scrim), it checks the parent chain:

1. If an ancestor carries `cuvara-theme-root` or `cuvara-theme-local`, the component inherits
   from it and attaches nothing.
2. If not, it attaches the default sheet to its own top element and marks that element
   `cuvara-theme-local`.
3. If the sheet cannot be loaded at all (stripped or not imported), the component applies the
   exact old inline styles instead. Classes are added in every case.

**Why this choice:**

- `ConfirmDialog.ShowAsync` is a static call and `ToastService` a plain constructor. Hosts use
  both without a container or a `RootUIDocument`, and tests use them on detached elements.
  `Resources.Load` is the only route by which runtime code reaches a package asset without the
  host handing it over.
- A serialized reference alone (on `RootUIDocument`) would leave those callers unstyled.
- The inline fallback keeps step 3 a no-regression path, not a blank dialog.

**Removing local copies.** Each local copy declares the `:root` defaults again on its own
element, which would shadow overrides set higher up. So `ICuvaraThemeService.Apply(root)` removes
local copies from every element below `root`. A component created before the theme was applied
is therefore fixed by the next `Apply`.

`Apply` is idempotent. Call it again after you move such a component under a themed root.

## The theme service

```csharp
public interface ICuvaraThemeService
{
    void Apply(VisualElement root);                        // attach to a root; idempotent
    void Remove(VisualElement root);                       // stop syncing a root, detach its sheets
    void AddOverride(StyleSheet sheet);                    // per-game sheets, in order; idempotent
    void RegisterModeSheets(params StyleSheet[] sheets);   // every mode sheet the game uses
    void SetModeSheet(StyleSheet sheet);                   // switch the mode; null removes it from roots
    IDisposable AttachScreen(VisualElement screenRoot);    // keep an imported screen on the current mode
}
```

`Remove(root)` detaches the base, override and mode sheets this service attached, removes
`cuvara-theme-root`, and stops syncing that root. A root that was never applied is ignored.
Call it when a themed root outlives its use, so the service does not keep it.

On every applied root the order is: other host sheets, then the base theme, then the overrides
in the order added, then the mode sheet. At equal specificity a later sheet wins. So an override
that sets `--cuvara-*` on `:root` replaces the default, and a mode sheet replaces both. Changes
made after `Apply` (a new override, a mode switch) are synced into every applied root.

`CuvaraThemeService()` uses the package sheet as the base. `CuvaraThemeService(StyleSheet)`
replaces it; a replacement base must include the component rules. Prefer overrides.

`RootUIDocument` needs no host code:

- It owns one service (`RootUIDocument.Theme`).
- It applies the service to its `rootVisualElement` when its layers resolve.
- It reads three serialized fields:
  - `applyTheme` (default on; off means you call `Theme.Apply` yourself);
  - `themeOverrides` (per-game sheets, in order);
  - `themeModeSheet` (initial mode).

`RegisterUIToolkit()` registers `ICuvaraThemeService` as that same instance, so a presenter can
inject it and switch modes.

## Overriding from a game: the bridge sheet

A game does not edit `CuvaraTheme.uss`. It adds sheets after it.

The direct way:

```css
/* MyGame/Theme/cuvara-overrides.uss */
:root {
    --cuvara-color-primary: rgb(255, 140, 0);
    --cuvara-radius-md: 12px;
}
```

The Figma way. The connector writes `tokens.uss` and `<mode>.uss` into its import folder; never
edit them. The game owns a **bridge sheet** that holds aliases only:

```css
/* MyGame/Theme/cuvara-theme-bridge.uss: outside Assets/Figma/<file>/ */
:root {
    --cuvara-color-surface: var(--background-primary);
    --cuvara-color-on-surface: var(--text-primary);
    --cuvara-color-primary: var(--brand-primary);
    --cuvara-color-scrim: var(--overlay-scrim);
    --cuvara-radius-md: var(--radius-medium);
}
```

Wire it on `RootUIDocument`:

- `themeOverrides` = `[tokens.uss, cuvara-theme-bridge.uss]`;
- `themeModeSheet` = `light.uss`.

The order is required. `tokens.uss` must be attached at the same root so that its variables
exist where the bridge resolves them. The connector's own `<Style src="tokens.uss"/>` links sit
on each imported screen, which is a subtree. The dialog, toasts and scrim live in the overlay
layer, outside that subtree.

## Mode sheets (light / dark)

```csharp
theme.SetModeSheet(darkSheet);   // replaces the previous mode sheet on every applied root
theme.SetModeSheet(null);        // back to tokens and overrides only
```

A Figma mode sheet redefines the Figma tokens, for example `--background-primary`. Because the
bridge aliases `--cuvara-*` to those tokens, the built-in UI follows the mode with no extra rule.
The mode sheet is always last, so it wins over `tokens.uss` at equal specificity.

### Imported screens and their mode sheets

The connector links the mode a screen resolved to inside the screen itself
(`<Style src="tokens.uss"/>`, `<Style src="light.uss"/>`). Those sheets sit on the screen's
elements, below the document root, so their token values win over the root's mode sheet inside
the screen. Switching the root alone would leave an open imported screen in the old mode.

The service handles this; no game code re-links sheets:

```csharp
theme.RegisterModeSheets(lightSheet, darkSheet);    // once: every mode sheet the game uses
var attachment = theme.AttachScreen(view.Root);      // per imported screen, while it is open
// ...
attachment.Dispose();                                // when the screen closes
```

- `AttachScreen` replaces, inside the screen, every registered mode sheet with the current mode
  sheet, in the same position, now and on every later `SetModeSheet`. Elements that link no
  mode sheet are left alone.
- A sheet passed to `SetModeSheet` is registered automatically. Register the others up front, so
  a screen that links `light.uss` is recognised while the current mode is `dark.uss`.
- With **no current mode** (none set yet, or after `SetModeSheet(null)`), the service has no
  opinion: attached screens keep the mode sheet they link at that moment. So
  `SetModeSheet(null)` clears the mode from the roots but leaves screens on their last mode.
- Dispose the attachment when the screen closes (in a presenter, `subs.Add(theme.AttachScreen(View.Root))`).
  Disposing stops tracking; it does not change the screen's sheets.

The Figma Import sample (`Samples~/FigmaImport`, `FigmaModeSwitcher.cs`) shows the pattern.

## Relation to the Figma connector's token naming (audit §2)

The connector names a token `--` + the lowercased variable name, with runs of non-alphanumerics
collapsed to `-`. It drops the collection name: variable `Background/Primary` in collection
`Colors` becomes `--background-primary`. Aliases become
`var(--target)`, colours become `rgba()`, and numbers become `px`. String and boolean variables
are dropped.

Consequences for the bridge:

- Bridge entries are `--cuvara-<role>: var(--<figma-token>);`. They never contain a literal, so
  a re-import that changes a value needs no bridge change.
- The `--cuvara-` prefix cannot collide with a Figma token unless a Figma variable is literally
  named `cuvara-...`. Do not name Figma variables that way.
- Unbound Figma properties are hard-coded literals in the generated screen USS. They do not
  flow through tokens, so they cannot be bridged. The validator (G5) reports them.
- The per-game role map that produces the bridge (G2/G7) is Editor tooling. This runtime
  contract only defines the role names in the table above.

## What is not themable yet

- **Sizes.** Dialog `min-width: 300px` / `max-width: 500px` and toast width `300px` are literals
  in the component rules. Override the rule if you must.
- **Toast placement.** The stack is top-right, 20 px in. The offset follows `--cuvara-space-lg`;
  the corner does not.
- **Fonts.** No `-unity-font-definition` is set. The built-in UI uses the panel's font.
- **Button states beyond the confirm button.** The confirm button keeps its colours on hover,
  active and focus, as the old inline style did. Disabled state and the cancel button use the
  panel's default Button theme.
- **Motion.** There are no transitions; toasts appear and vanish instantly.
- **A transparent scrim** (`CloseOnTapOutside` without `DimsBelow`) carries `cuvara-scrim` but
  no colour, by design.

## Known gaps in verification

- No test resolves a rendered colour. Package tests run on detached elements (see
  `Documentation~/CI.md`). They check classes, attached sheets, inline fallback values and the
  `.uss` text. A real panel resolving `var()` through the `:root` block is observed only in a
  running game.
- Unity may store a USS colour with 8-bit channel precision. If so, a fractional default like
  `38.25` renders within 0.5/255 of the old float, which is not visible. The inline fallback
  path is exact.
