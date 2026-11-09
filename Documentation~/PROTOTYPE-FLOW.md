# Prototype runner and the `flow.json` contract

`Cuvara.UIToolkit.Prototype` (`Runtime/Prototype/`) plays a clickable prototype from generated UI Toolkit screens. Each screen
is a UXML file plus an optional `<Screen>.flow.json` that says what its elements do: navigate, open/toggle/close an overlay,
go back, wait, select in a list, scroll by drag. The runner was written for penpot-uitoolkit-bridge, whose converter writes the
flow files from a Penpot prototype; it moved here in 0.9.0 ([PENPOT-BRIDGE-MOVE.md](PENPOT-BRIDGE-MOVE.md)). The runner knows
nothing about Penpot or a game, and its assembly references nothing but UnityEngine.

The runner is for prototypes, previews and tests. A game screen uses its own presenter (or the bridge's generated
`<Screen>Bindings` class), not the runner.

## Using the runner

```csharp
var go = new GameObject("Prototype");
go.AddComponent<UIDocument>();
var runner = go.AddComponent<PrototypeRunner>();
runner.AutoStart = false;
runner.Screens.Add(new PrototypeScreenEntry { Name = "Menu", Uxml = menuUxml, Flow = menuFlowJson });
runner.Screens.Add(new PrototypeScreenEntry { Name = "Detail", Uxml = detailUxml, Flow = detailFlowJson });
runner.StartScreen = "Menu";
runner.Begin();
```

| Member | What it does |
|---|---|
| `Screens`, `StartScreen`, `AutoStart` | The screens by name; the first screen is `StartScreen`, else the `start` of a flow, else the first entry. `AutoStart` calls `Begin()` in `Start()`. |
| `PanelSettingsAsset`, `Theme`, `CreateDefaultPanelSettings`, `ConfigurePanelSettings` | Panel settings: the assigned asset (only read, never written), else a created 1920x1080 `ScaleWithScreenSize` instance (match 0.5) with `Theme`. |
| `UseRenderTarget(w, h)`, `RenderTarget`, `OwnedPanelSettings` | Render into a RenderTexture through a PanelSettings copy the runner owns and destroys. |
| `Animate` | Play the interaction animations (dissolve, slide, push) with USS transitions. Off: navigation is instant and every `*.motion` style sheet is detached (`MotionSheets.Detach`). |
| `Navigate`, `Back`, `ShowScreen`, `OpenOverlay`, `CloseOverlay`, `CloseAllOverlays`, `FinishTransition` | Navigation from code; the same operations the flow file triggers. |
| `CurrentScreen`, `ScreenRoot`, `Host`, `OverlayCount`, `OverlayNames`, `OverlayRoot`, `HistoryCount`, `IsTransitioning`, `LastTransition` | State. |
| `ScreenShown`, `OverlayShown`, `OverlayClosed`, `ListSelectionChanged`, `InteractionRequested`, `InteractionFired`, `TransitionFinished` | Events. |
| `Select(list, item)`, `SelectedClass` (`selected`), `SelectedElementName` (`Selected`), `SelectionMarkClass` (`selection-mark`) | Single selection in a list. |

Other public types: `UiInputSimulator` (real pointer, wheel and key events sent through the panel, for tests and gates),
`UiClicks` (a click on a Button or on any element), `ScreenTransitionSignal` (the "transition is over" signal, below) and
`MotionSheets` (removes `*.motion` style sheets).

Behaviour that is easy to miss:

- **Input is blocked while a transition runs.** A transparent element named `PrototypeInputBlock` covers the document. A
  click during a transition is dropped (`InteractionRequested` reports it); an `after-delay` interaction waits for the end.
- **A transition ends exactly once**, at the native `TransitionEndEvent` of its longest track, at a safety timer, or when the
  next request completes it early. The runner then sends `ScreenTransitionSignal` to the root of the screen or overlay that came
  or went: a `TransitionEndEvent` with a negative `elapsedTime` (native events never have one), so a listener needs no reference
  to this assembly.
- **Pointer warm-up.** Under Mono the first enter/leave dispatch of UI Toolkit JIT-compiled two generic helpers for 1.3-2.7 s
  (measured in the bridge, 6000.3.9). `Begin()` compiles them on a worker thread with `MethodHandle.GetFunctionPointer`; nothing
  is executed off the main thread. IL2CPP has nothing to warm.
- **Class names `selected` and `selection-mark` have no `cuvara-` prefix.** They are not this package's styling: the generated
  screen's style sheet (owned by the game, with the game's prefix on everything else) styles them, and the flow contract fixes
  them. They are part of the contract below and do not change within a major version.
- **Element names of the host tree:** `PrototypeHost` (screens), `PrototypeOverlays` (overlay layer), `PrototypeOverlay-<name>`
  (one overlay with its scrim), `PrototypeInputBlock`.

## The `flow.json` contract

Machine-readable schema: [prototype-flow.schema.json](prototype-flow.schema.json) (JSON Schema 2020-12). The writer is
penpot-uitoolkit-bridge `bridge/flow.py` (`build_flow`); every fixture under `Tests/Runtime/Prototype/Fixtures/` and the five
flow files the bridge synced into the client (`Assets/UI/PenpotTest/*/`, 2026-11-09) validate against it.

### Version rule

- `version` is an integer: the **major** version. The current and only version is **1**
  (`PrototypeFlowFile.SupportedVersion`).
- The runner rejects a file whose `version` is not 1, and a file without `version` (it reads as 0):
  `PrototypeFlowFile.Parse` throws `FormatException`, and `Begin()` logs `[Cuvara.UIToolkit] flow of <screen>: Unsupported or
  empty flow file (version N, this runner reads version 1)` and plays that screen without behaviour.
- **Within a major, changes are additive only**: a new optional field that an older runner may ignore. The runner ignores
  every field it does not know; the converter already writes several for the bridge's gates (`controls[].size`, `component`,
  `label`, `shapeId`, `drawnState`, `variants`, `labelColors`, `crossfade`, `lists[].selectionMotion`).
- **A new major** is needed to remove or rename a field, change a field's type or meaning, or add a trigger, action or
  animation type the runner must understand to play the file correctly. It needs a runner release that reads it; the writer
  must not emit it before.

### Fields of version 1

Top level:

| Field | Type | Runner use |
|---|---|---|
| `version` | integer, `1` | required; see above |
| `screen` | string | the screen's name (matches `PrototypeScreenEntry.Name`) |
| `overlay` | bool | informational: another screen opens this one as an overlay |
| `size` | `[w, h]` | informational: board size in px |
| `flows` | `[{name, start}]` | `start` picks the first screen when `StartScreen` is not set |
| `interactions` | array | wired on the screen's elements (below) |
| `controls` | `[{name, type, enabled, motion?}]` | not read by the runner (the bridge's gates read it) |
| `lists` | `[{name, kind, template, selectable}]` | `selectable`: single selection; a ScrollView list also gets drag scrolling |
| `scrolls` | `[{name, mode}]` | the named ScrollView gets drag scrolling |
| `motion` | `{back, enabled}` | `back: reverse` (default) plays the reverse of the navigation `previous-screen` undoes, `none` is instant; `enabled: false` animates nothing |

`interactions[]`:

| Field | Values | Meaning |
|---|---|---|
| `id` | `<Screen>.<element>.<trigger>#<index>` | stable id |
| `element` | element name | where the trigger is wired; an unknown name logs a warning |
| `trigger` | `click`, `mouse-enter`, `mouse-leave`, `after-delay` | `click` uses `UiClicks`; a disabled element (not `enabledInHierarchy`) fires nothing |
| `delayMs` | number or null | `after-delay` only |
| `action` | `navigate-to`, `open-overlay`, `toggle-overlay`, `close-overlay`, `previous-screen` | an unknown action logs a warning |
| `target` | screen name or null | destination; `close-overlay` without one closes the overlay that contains the element |
| `position` | `center` (default), `top-left`, `top-center`, `top-right`, `bottom-left`, `bottom-center`, `bottom-right`, `manual` | overlay placement |
| `manual` | `[x, y]` or null | `manual` position in px |
| `closeOnOutside`, `scrim` | bool or null | overlay: a press outside closes it (playing the open animation inverted); a 50 % black scrim |
| `animation` | object or null | below |

`interactions[].animation` (`PrototypeAnimation`):

| Field | Values | Meaning |
|---|---|---|
| `type` | `dissolve`, `slide`, `push` | anything else is not animated |
| `durationMs` | number | below 1: not animated |
| `easing` | `linear`, `ease`, `ease-in`, `ease-out`, `ease-in-out` | UI Toolkit easing; anything else reads as `ease-out` |
| `way` | `in`, `out` or null | slide between screens: the new screen slides in over the old one, or the old one slides out above the new one |
| `direction` | `left`, `right`, `up`, `down` or null | slide and push: the direction of motion |
| `offsetEffect` | bool or null | slide between screens: the other screen drifts by 20 % |

Overlays ignore `way` and `offsetEffect`, and `push` on an overlay is instant (Penpot's viewer does the same). A `close-overlay`
fired by `mouse-leave` closes without animation.

## Tests

`Tests/Runtime/Prototype/` (`Cuvara.UIToolkit.Prototype.Tests`, PlayMode): the runner, transitions, ownership of the
render target and panel settings, drag and wheel scrolling, the motion helpers driven by real pointer events, and the
contract above (`PrototypeFlowContractTests`). The fixtures are flow files in the converter's format and a theme that imports
Unity's default runtime theme, so the suite needs neither the bridge nor the client.
