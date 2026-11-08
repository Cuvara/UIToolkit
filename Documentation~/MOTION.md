# Motion helpers

Assembly `Cuvara.UIToolkit.Motion` (`Runtime/Motion/`), namespace `Cuvara.UIToolkit.Motion`. No assembly references, no
Editor references, no game rules; it does not depend on the rest of this package, so it can be referenced on its own.
Tests: `Cuvara.UIToolkit.Motion.Tests` (`Tests/Runtime/Motion/`, EditMode, 92 cases).

The API was built and tested in penpot-uitoolkit-bridge and copied here unchanged apart from the namespace, the assembly
name and the asset menu path (see [PENPOT-BRIDGE-MOVE.md](PENPOT-BRIDGE-MOVE.md) for the name mapping).


Game-driven UI animation: a small, allocation-conscious API a presenter uses on the elements of its screen (for example the
fields of a generated UXML binding class or `Require<T>` results). No dependencies, no Editor references, no game rules. Everything is
built with `UiMotion` and returns a `MotionClip` (a description; nothing runs until `.Play()`); `Play` returns a `MotionHandle`.

```csharp
using Cuvara.UIToolkit.Motion;

public sealed class MenuPresenter
{
    readonly MenuBindings m_Ui;                                                 // any typed binding of the screen's UXML
    MotionHandle m_Pulse;

    public MenuPresenter(VisualElement root)
    {
        m_Ui = MenuBindings.Bind(root);
        m_Ui.GoButtonClicked += OnGo;
        m_Pulse = MotionExamples.PulseLoop(m_Ui.GoButton).Play();             // loops until cancelled
        MotionExamples.StaggerIn(m_Ui.Grid.Children()).Play();                 // list items rise in one by one
    }

    async void OnGo()
    {
        m_Pulse.Cancel();                                                       // keeps the current scale
        var result = await UiMotion.Parallel(
                UiMotion.Fade(m_Ui.Menu, 0f, MotionTiming.Normal),
                UiMotion.Move(m_Ui.Menu, new Vector2(0f, 24f), MotionTiming.Normal))
            .Play(new MotionOptions { BlockInput = true });                    // no pointer event reaches the UI meanwhile
        if (result == MotionResult.Completed)
            Navigate();
    }
}
```

**Primitives** (`UiMotion`): `Fade`, `Move` (USS `translate`, px), `Scale`, `Rotate` (degrees), `Tint` (background, image tint or
text colour) and `Tween` (several channels in one tween). Each takes a `MotionTiming` (`DurationMs`, `DelayMs`, `MotionEasing`;
`MotionTiming.Fast/Normal/Slow` are 120/200/320 ms ease-out) and an optional start value; without one the tween starts from the
current value of the element. Easing is one of the five CSS names (`Linear`, `Ease`, `EaseIn`, `EaseOut`, `EaseInOut`, same
curves as CSS and USS) or `MotionEasing.CubicBezier(x1, y1, x2, y2)` (all four values finite, x1 and x2 in [0, 1]). There is deliberately no way to animate width, height,
margin, padding, left/top, flex properties, font-size or slices: they relayout or regenerate geometry every frame (measured on
Unity 6000.3 when the API was designed in penpot-uitoolkit-bridge); the channel list (`MotionChannels`) is the allowed set.

**How it runs.** A tween without a start value, with a named easing, outside a loop runs as a USS transition: the helper declares
`transition-*` inline, writes the end value and the style system animates it (no C# per frame). Everything else (a start value, a
bezier, loops, count-up) writes inline styles frame by frame from a wall-clock timeline. `MotionOptions.Mode = Manual` forces
frame by frame. A transition needs a frame between the start state and the end value, so a run that contains transitions starts
two frames after `Play`. Both paths follow the wall clock (`Time.realtimeSinceStartup`), like USS transitions, not `Time.timeScale`.

`transition-*` are element-wide lists, so motion keeps a per-element ledger: every run (and every target object) that animates
an element adds its own entries, with its own duration and easing, to one merged inline declaration, and removes only its own
when it ends. What the element declared before (inline, or from a style sheet) stays in force for properties motion does not
animate. A frame-by-frame write must jump, not animate, so while a run drives a channel that channel is declared with a 0 ms
transition; the previous declaration comes back one frame after the run ends, so it cannot animate the last write. Each run has
its own frame driver: one `UiMotion.Target(element)` object can serve any number of runs.

**Composition**: `Sequence`, `Parallel`, `Stagger(items, build, stepMs)` / `Stagger(clips, stepMs)`, `Loop(clip, count, pingPong)`
(`MotionLoop.Infinite` for ever), `Wait(ms)` and `Call(action)` (a callback at a point of the timeline). A clip plays once and
can be in one group only; build a new one for each run. Inside a loop the start value is captured once, in the first iteration,
and every iteration starts from it. Each iteration plays its `Call`s once, including a call at the very end of the iteration; when
one long frame skips iterations, the calls of the skipped iterations still fire (at most 16 per frame, older ones are dropped) and
`Complete()` fires the calls that are left. A ping-pong loop (every second iteration plays backwards) cannot contain a `Call`,
directly or nested: it is not defined when a call fires on the way back, so `Loop(..., pingPong: true)` throws an
`ArgumentException` at build time. Use `handle.OnFinished` for the end of the whole loop, or put the `Call` outside the ping-pong loop.

**Control** (`MotionHandle`): `Cancel(MotionCancel.Keep | Restore)`, `Complete()`, `Result`
(`Running`, `Completed`, `Cancelled`, `Detached`), `OnFinished(callback)`, `yield return handle` and `await handle`. End states, exactly:

| Situation | What stays on the elements |
|---|---|
| run reaches its end, or `Complete()` | the end values (inline); an infinite loop completes as one iteration |
| `Cancel()` / `Cancel(Keep)` | the values as they are right now (a transition is read from `resolvedStyle` and pinned inline) |
| `Cancel(Restore)` | the value each property had when the run first touched it |
| an element leaves its panel (also when the panel is destroyed) | the end state is applied to that element and it is no longer driven; the run ends `Detached` when no element is left |
| `Play` on an element that is not in a panel | the end state is applied at once, the handle is already `Detached` |
| a new run animates the same property of the same element | the older run is cancelled with `Keep`; runs on other properties are untouched |

A run gives back its transition claims on every exit path. `UiMotion.ResetStyles(element)` removes the inline values motion may
have left and releases any claim still held on the element (it does not cancel a run).

**Input blocking.** `MotionOptions.BlockInput = true` puts one transparent full-panel element (`motion-input-blocker`) on top of
the panel while the run is active; overlapping runs share it, and it is removed when the last one ends or is cancelled
(owner decision: pointer input is blocked until a transition ends). Pass `BlockRoot` to choose the panel.

**Presets.** Named timings and state values reach code as JSON that `JsonUtility` reads, so presets a design tool defines (the
penpot-uitoolkit-bridge converter writes this file from its `motion` profile section) also drive code animations:

```json
{ "version": 1,
  "durations": [ { "name": "fast", "ms": 120 }, { "name": "normal", "ms": 200 } ],
  "presets": [ { "name": "button", "duration": "fast", "easing": "ease-out", "delayMs": 0,
                 "properties": [ "scale", "translate", "opacity" ], "crossfade": false,
                 "states": [ { "state": "hover", "scale": "1.04" },
                             { "state": "pressed", "scale": "0.96", "translate": "0 2px" },
                             { "state": "disabled", "opacity": "0.6" } ] } ] }
```

```csharp
MotionPresets.Load(jsonTextAsset);                       // or a MotionPresetAsset (Create > Cuvara > UI Toolkit > Motion Presets): asset.Apply()
UiMotion.Fade(popup, 1f, MotionPresets.Timing("button")).Play();               // the designer's duration and easing
MotionPresets.TryGet("button", out var button);
button.To(card, "pressed").Play();                                             // tween to the state's end values
button.To(card, MotionPreset.RestState).Play();                                // identity for the preset's non-colour properties
```

Validation on load (each problem is logged as an error and the preset is skipped): only `opacity`, `translate`, `scale`, `rotate`
and colour properties are allowed (`all`, layout and geometry properties are rejected), a state may only set properties the preset
lists, `translate` is px only, easing is a named one or `cubic-bezier(...)`, `duration` is a name from `durations` or a number of ms.
A missing preset name logs a warning and falls back to `MotionTiming.Normal`. `crossfade` is carried for penpot-uitoolkit-bridge and ignored here.
The format above is the contract; `version` is 1.

**Examples** (`MotionExamples`, generic): `Pop` (scale 0.8, 1.05, 1 with a fade in), `Shake`, `PulseLoop`, `StaggerIn` (fade and
rise, one after another) and `CountUp(label, from, to)` (writes the text only when the number changes).

**Cost.** Per frame a manual run does one `Vector4` lerp and one inline style write per channel and allocates nothing in the
engine; a transition run does no per-frame work except one timeline check. A run costs a few small objects (clip tree, handle,
one detach callback per element) at `Play`. The Unity 6000.3 cost numbers for the allowed properties are in the
penpot-uitoolkit-bridge animation spike (`docs/SPIKE-ANIMATION.md` there, section 4); phone frame time was not measured.
