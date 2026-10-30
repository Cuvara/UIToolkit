# Cuvara UI Toolkit

A **standalone UI Toolkit screen layer** for Unity 6. It gives you view lifecycle, parenting
into a `UIDocument`'s visual tree, collection adapters, safe-area and panel-scale handling,
and a back-navigation event source — and it depends on no UI framework of its own.

It owns its contracts. A host binds it to whatever screen flow it already has.

On top of that core it ships:

- a screen flow (`ScreenNavigator`, presenters, transitions, `ConfirmDialog`, `ToastService`)
  with VContainer registration;
- a theme contract: the built-in UI reads `--cuvara-*` USS variables a game can override
  ([THEMING.md](Documentation~/THEMING.md));
- `Require<T>` typed queries and a UXML-to-C# binding codegen
  ([UXML-CODEGEN.md](Documentation~/UXML-CODEGEN.md));
- hybrid runtime data binding and an optional DOTS/ECS presentation adapter;
- an optional integration with Unity's **UI Toolkit Connector for Figma**: per-game profiles,
  a token bridge into the theme, a component mapping preset, a diagnostics window (including
  the notes of headless imports), an import validator and screen scaffolding over imported UXML
  ([FIGMA-INTEGRATION.md](Documentation~/FIGMA-INTEGRATION.md)).

## Documentation

| Page | Read it for |
|---|---|
| [THEMING.md](Documentation~/THEMING.md) | the `--cuvara-*` tokens, classes, theme service and override pattern |
| [UXML-CODEGEN.md](Documentation~/UXML-CODEGEN.md) | `Require<T>`, typed UXML bindings, the drift check |
| [HYBRID-DATA-BINDING.md](Documentation~/HYBRID-DATA-BINDING.md) | runtime data binding behind the view interfaces |
| [LIFECYCLE-PLAN.md](Documentation~/LIFECYCLE-PLAN.md) | screen lifecycle design |
| [FIGMA-INTEGRATION.md](Documentation~/FIGMA-INTEGRATION.md) | connector setup, profiles, token bridge, mapping preset, diagnostics, supported Figma features |
| [FIGMA-VALIDATION.md](Documentation~/FIGMA-VALIDATION.md) | validator rules (`FIG0xx`), the `validate` CLI verb, the headless import helper |
| [TROUBLESHOOTING-FIGMA.md](Documentation~/TROUBLESHOOTING-FIGMA.md) | connection problems and what to do about each diagnostic code |
| [FIGMA-REAL-IMPORT.md](Documentation~/FIGMA-REAL-IMPORT.md) | connector compatibility record, saved import results, unsupported constructs, the real-import lab |
| [FIGMA-CONNECTOR-AUDIT.md](Documentation~/FIGMA-CONNECTOR-AUDIT.md) | why the integration is built the way it is, and its status |
| [MIGRATION-0.8.md](Documentation~/MIGRATION-0.8.md) | upgrading from 0.7.2 |
| [CI.md](Documentation~/CI.md) | what CI checks, and what it cannot |
| [RELEASE-TEST-MATRIX.md](Documentation~/RELEASE-TEST-MATRIX.md) | every test suite, where it runs, and the release checklist |

## Samples

Import them from **Window > Package Manager** > Cuvara UI Toolkit > **Samples**.

| Sample | Shows |
|---|---|
| Screen Flow (scene) | push, pop, replace, pop-to-root, a dimming modal and Back, with a live lifecycle HUD |
| Notification Popup | the smallest complete screen, with no host framework |
| ECS HUD | a HUD driven from ECS through the adapter, and the reference hybrid data-binding screen (needs `com.unity.entities`) |
| Loading Flow | a two-scene loading-to-main flow using every navigator feature |
| Figma Import | a game adopting the Figma connector: an imported screen with View and Presenter, the theme fed by Figma tokens, light/dark switching; runs without the connector |

## It does not depend on GameFoundation

This is the design constraint, not an accident of packaging.

The package was extracted from a GameFoundation branch, where every one of its files
referenced `com.gdk.core` — `IScreenView`, `IScreenManager`, `BaseScreenPresenter`,
`SignalBus`, `IAssetsManager`, `ILoggerManager`. **All of those references were severed.**
The dependency now runs one way: `com.gdk.core` references this package, through an adapter
that lives on the GameFoundation side.

| Lives here | Lives in the host |
|---|---|
| `IViewLayer`, `IViewSurface`, `IUIToolkitView` — the contracts | the screen-flow implementation that binds to them |
| `BaseUIToolkitView`, `UIToolkitViewFactory`, `VisualElementViewLayer` | presenter bases, if the host has presenters |
| `RootUIDocument` | which screens exist and when they open |
| collection adapters, safe area, panel scale | the back-navigation *policy* — what Escape closes |
| a back-navigation **event source** | |

The back-navigation split is the sharpest example. This package registers for
`NavigationCancelEvent` on the panel and raises a plain C# event. It does not decide what to
close and it does not open a quit dialog, because "what does Back mean" is an application
question. A host that wants the GameFoundation behaviour writes ten lines against the event.

**Why it matters practically:** a package that reaches into its host cannot be installed,
compiled or tested on its own. This one can — see [Documentation~/CI.md](Documentation~/CI.md),
where CI bootstraps a throwaway project from this package's own declarations and runs the
suite. That is only possible because nothing here needs a private submodule to resolve.

## Requirements

- Unity **6000.3** or later.
- UniTask `2.5.10` and VContainer `1.16.9`, from OpenUPM (declared dependencies; see below).
- Optional: `com.unity.entities` `1.4.8` for `Runtime/Ecs/`.
- Optional: `com.unity.ui.figma` `0.3.0-exp.1` (tested) for the Figma integration.

## Install

Add the package to `Packages/manifest.json` by git URL, pinned to a release tag:

```jsonc
{
  "scopedRegistries": [
    { "name": "OpenUPM", "url": "https://package.openupm.com", "scopes": ["com.cysharp", "jp.hadashikick"] }
  ],
  "dependencies": {
    "com.cuvara.uitoolkit": "https://github.com/Cuvara/UIToolkit.git#v0.8.0"
  },
  "testables": ["com.cuvara.uitoolkit"]
}
```

`testables` is optional; it runs the package's tests in your project. Releases are the
`v*` tags of this repository, and `CHANGELOG.md` lists what each one changed. The release
workflow also publishes an npm copy to GitHub Packages as `@cuvara/uitoolkit`; Unity projects
install from the git URL above.

The OpenUPM scoped registry covers UniTask and VContainer. **A UPM package cannot declare a
scoped registry of its own**, so this belongs to the consuming project and there is no way to
ship it from here. That is also why `com.frostbun.*` is not a dependency: asset loading goes
through `IVisualTreeAssetLoader`, which you implement over Addressables, `Resources`, or
whatever you already use.

**VContainer is required, not optional.** An earlier draft gated the registration assembly
behind a versionDefine so the package would install without it. That gating is
gone — this project standardises on VContainer for all dependency injection, so "no container"
is not a supported configuration and the branch existed without anything exercising it.

### Optional: the Figma connector

Only needed if your game imports UI from Figma. The connector is Unity's experimental
**UI Toolkit Connector for Figma design**; this package never imports or converts Figma data
itself.

1. **Window > Package Manager** > **+** > **Add package by name**: `com.unity.ui.figma`, version
   `0.3.0-exp.1` (the tested version). It installs `com.unity.pipeline` too. This package's gated
   assembly `Cuvara.UIToolkit.Editor.Figma` then compiles and the **Window > Cuvara > Figma
   Diagnostics** and **Tools > Cuvara > Figma** menus appear.
2. In Figma, run the Community plug-in **Unity UI Toolkit Connector** (free, Dev or Full seat).
3. **Edit > Project Settings > Pipeline > Editor**: enable **Allow Browser Clients**, then
   restart the Pipeline server.
4. **Window > UI Toolkit > Connector for Figma design** > **Copy token**, and paste it into the
   plug-in's **Settings** tab within 60 seconds. Copy it again after every Editor restart.

Then create a Figma profile and follow [FIGMA-INTEGRATION.md](Documentation~/FIGMA-INTEGRATION.md#setup).
If something does not connect: [TROUBLESHOOTING-FIGMA.md](Documentation~/TROUBLESHOOTING-FIGMA.md).

### A screen's lifetime is a child scope

This is the whole lifecycle story, and it is worth stating because getting it wrong is the
usual source of UI leaks.

```csharp
// open
var scope = container.CreateScope(b =>
{
    b.Register<InventoryView>(Lifetime.Scoped).As<IInventoryView>();
    b.RegisterEntryPoint<InventoryPresenter>();   // IStartable + IDisposable
});

// close
scope.Dispose();
```

One `Dispose()` unsubscribes the presenter's handlers, releases screen-scoped services and
tears down the view — together, structurally, rather than each being something a person has to
remember. The presenter stays a plain C# class with no `UIDocument` and no `VisualElement`, so
it is testable against mocked view and service interfaces with no scene involved.

## Using it

```csharp
// 1. A layer is somewhere a view can be parented. RootUIDocument gives you three.
var layers = rootUIDocument.Layers;         // Screen, Hidden, Overlay

// 2. A view is a UXML document plus a lifecycle.
public sealed class MyScreenView : BaseUIToolkitView
{
    private readonly Button closeButton;

    public MyScreenView(VisualTreeAsset asset) : base(asset)
    {
        this.closeButton = this.Root.Q<Button>("close");
    }
}

// 3. The factory loads the UXML through YOUR loader and constructs the view.
var view = await factory.CreateAsync<MyScreenView>("MyScreen");
view.ViewSurface.SetParent(layers.Screen);
await view.Open();
```

`IVisualTreeAssetLoader` is the one thing you must supply:

```csharp
public sealed class AddressablesVisualTreeAssetLoader : IVisualTreeAssetLoader
{
    public UniTask<VisualTreeAsset> LoadAsync(string key) => /* your loader */;
}
```

## What is here

| Path | Contents |
|---|---|
| `Runtime/Core/` | `IViewLayer`, `IViewSurface`, `IUIToolkitView`, `IVisualTreeAssetLoader`, `IPresenterInstantiator` |
| `Runtime/View/` | `BaseUIToolkitView`, `UIToolkitViewFactory`, `VisualElementViewLayer` |
| `Runtime/ViewModel/` | `BindableViewModel` — the notifying base for hybrid data-binding ViewModels |
| `Runtime/Managers/` | `RootUIDocument` and the default three-layer `RootUIDocument.uxml` |
| `Runtime/Flow/` | `ScreenNavigator`, screen presenters and options, transitions, `ConfirmDialog`, `ToastService` |
| `Runtime/VContainer/` | `RegisterUIToolkit()` and screen-flow registration (`Cuvara.UIToolkit.VContainer`) |
| `Runtime/TestSupport/` | fakes and assertions for testing presenters (`Cuvara.UIToolkit.TestSupport`) |
| `Runtime/Collections/` | list, grid and multi-template adapters + item view/presenter bases |
| `Runtime/Utilities/` | `SafeAreaElement`, `SafeAreaCalculator`, `PanelScaleRatio`, `Require<T>` |
| `Runtime/Input/` | back-navigation event source |
| `Runtime/Theme/` | `CuvaraTheme.uss` (`--cuvara-*` theme contract), `ICuvaraThemeService` — see [THEMING.md](Documentation~/THEMING.md) |
| `Runtime/Ecs/` | DOTS/ECS presentation adapter — optional, needs `com.unity.entities` |
| `Editor/Codegen/` | UXML → typed view codegen (menu + auto-regen); `Core/` is Unity-free |
| `Editor/ScreenCreator/` | **Assets > Cuvara > Create Screen**: view, presenter, model and test skeleton, for a new UXML or an existing (imported) one |
| `Editor/FigmaCore/` | Unity-free validator for `com.unity.ui.figma` import folders — see [FIGMA-VALIDATION.md](Documentation~/FIGMA-VALIDATION.md) |
| `Editor/Figma/` | optional Figma connector integration (profiles, token bridge, mapping preset, diagnostics); compiles only with `com.unity.ui.figma` — see [FIGMA-INTEGRATION.md](Documentation~/FIGMA-INTEGRATION.md) |
| `Tools~/UxmlCodegenCli/` | plain-`dotnet` CLI: drift check over the committed generated bindings, and `validate` for Figma import folders |
| `Tools~/FigmaImport/` | `figma-import.py`: headless Figma frame import through the connector's Pipeline commands; saves the import result for the diagnostics window. `test_figma_import.py` is its self-test |
| `Tools~/FigmaLab/` | `create-lab-project.py`: a throwaway Unity project for a real Figma import ([FIGMA-REAL-IMPORT.md](Documentation~/FIGMA-REAL-IMPORT.md)); `test_create_lab_project.py` is its self-test |
| `Samples~/ScreenFlow/` | a playable scene exercising push, pop, replace, modal and Back |
| `Samples~/NotificationPopup/` | the smallest complete screen, host-free |
| `Samples~/EcsHud/` | a HUD driven from ECS, through the adapter — and the reference hybrid data-binding screen |
| `Samples~/LoadingFlow/` | a two-scene loading-to-main flow using every navigator feature |
| `Samples~/FigmaImport/` | a Figma connector import (stand-in files) driving a screen, the theme bridge and light/dark modes; see its `README.md` |
| `Tests/Editor/` | EditMode tests: codegen, Screen Creator, the Figma validator core; `Tests/Editor/Figma/` runs only with the connector installed |
| `Tests/Runtime/` | runtime tests: views, flow, theme, collections and (with `com.unity.entities`) ECS — most need a live panel |

## Theming

The built-in UI (`ConfirmDialog`, `ToastService`, the navigator's tap-outside scrim) is styled by
`cuvara-*` classes reading semantic `--cuvara-*` variables. The defaults equal the old inline
values, and the stylesheet reaches those elements with no host work. A game re-skins them by
attaching a sheet that sets the variables, for example a bridge from its Figma tokens, and
switches light/dark with `ICuvaraThemeService.SetModeSheet`. Token table and override pattern:
[Documentation~/THEMING.md](Documentation~/THEMING.md).

## Typed queries and UXML codegen

`root.Require<Label>("popup-title")` is `Q<T>` that throws a precise
`InvalidOperationException` instead of returning null — the element name, the expected
type and the root searched under, in the message.

On top of it sits a codegen: enroll a UXML once via
**Assets/Cuvara/Generate UXML Bindings** and a `partial` class with one typed property per
named element plus an `AssignQueries(root)` appears in `Generated/` beside it, regenerated
automatically on every save (opt-in — only enrolled files are ever touched) and
byte-checked against its UXML by the `Tools~/UxmlCodegenCli` drift check, which you run in
your own CI. Your half of the partial
picks the base type and calls `AssignQueries` in the constructor. The full workflow — a
`ConfirmPopup` end to end, the naming/namespace conventions, the failure rules —
is in [Documentation~/UXML-CODEGEN.md](Documentation~/UXML-CODEGEN.md).

## Hybrid data binding

For data-heavy screens, Unity 6's runtime data binding is allowed — as a **View-internal
implementation detail** behind the existing `IView` interfaces, never above them. The
View assigns `Root.dataSource` and wires elements with C# `SetBinding` (`nameof` paths,
`BindingMode.ToTarget`); the Presenter writes plain properties on a
`BindableViewModel` (`Runtime/ViewModel/`) and never learns binding exists. Commands,
clicks and navigation stay on `ScreenSubscriptions`; and **a binding source must
notify** — a non-notifying source is version-polled by the binding system on every UI
update, which is the per-frame work this package forbids. The full convention, the
EcsHud walkthrough, the testing story and a per-screen decision table:
[Documentation~/HYBRID-DATA-BINDING.md](Documentation~/HYBRID-DATA-BINDING.md).

## DOTS / ECS

Optional. Install `com.unity.entities` and `Runtime/Ecs/` compiles; leave it out and that
assembly is skipped and nothing else changes.

**ECS does not touch UI Toolkit here, and the package will not let it.** The path is:

```
ECS world  ->  adapter  ->  ViewModel  ->  Presenter  ->  View  ->  UI Toolkit
```

`Runtime/Ecs/` is the **adapter half only**. It reads an unmanaged component, converts it to
a plain ViewModel, and pushes that to an `IViewModelSink<T>` the host implements — usually a
Presenter. It holds no view reference, names no Presenter type, and the assembly does not
reference UIElements at all. There is a test asserting that last point, because it is the
kind of boundary that erodes one convenient edit at a time.

```csharp
// 1. What the simulation writes. Unmanaged, Burst-friendly, knows nothing about UI.
public struct PlayerVitals : IComponentData { public int Health; public int MaxHealth; }

// 2. What crosses the boundary. Plain values — no VisualElement, no UIDocument.
public readonly struct VitalsViewModel { public readonly string Caption; /* ... */ }

// 3. The adapter. Convert() is usually the whole of a host bridge.
[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial class VitalsBridge : EcsViewModelBridge<PlayerVitals, VitalsViewModel>
{
    protected override VitalsViewModel Convert(in PlayerVitals c) => new($"{c.Health}/{c.MaxHealth}");
}

// 4. Your Presenter is the sink. It knows an IView, never a VisualElement.
public sealed class VitalsPresenter : IViewModelSink<VitalsViewModel>
{
    public void Push(in VitalsViewModel vm) => this.view.Render(vm.Caption);
}

// 5. Bind for the screen's lifetime; dispose with its scope.
var registration = EcsSinkRegistration.Bind(bridge, presenter);
```

### Two rules, constraining different things

Keep them apart — satisfying one does not satisfy the other.

**Architecture.** ECS must never manipulate UI Toolkit. That constrains *what the adapter may
talk to*: a ViewModel, never a view.

**Threading.** `VisualElement` is plain managed C#, **not** a `UnityEngine.Object`. It cannot
be touched from `ISystem`, `IJobEntity`, Burst, or any worker thread — there is no attribute,
no unsafe cast and no `NativeContainer` that makes it work. This is a type-system fact, not a
performance guideline, and any design that wants to write the visual tree from a job is
impossible rather than merely slow. It constrains *where the adapter runs*: `SystemBase`,
main thread, `PresentationSystemGroup`, never `[BurstCompile]`.

### It stays quiet when nothing changes

Two mechanisms, both cheap:

- `Enabled` is false while no sink is registered, so a world with no screen open does not
  even evaluate the query.
- The query carries `SetChangedVersionFilter`, so chunks nothing has written since the last
  run are skipped.

That filter is chunk-granular and conservative — it reports a chunk changed if anything wrote
the component, including writing an identical value. Override `HasChanged` for value-level
deduplication when a sink is expensive enough to be worth it.

Pushing every frame would defeat all of this, and it is the specific thing this design exists
to prevent: rebuilding a subtree at 60fps on the main thread is how UI Toolkit gets a
reputation for being slow when the fault is the caller's.

### Entity-to-sink mapping is by value

An `IComponentData` is unmanaged and cannot hold a `VisualElement`, a Presenter, or anything
else managed. Route by a value key — an entity index/version pair, or a stable game id — and
keep the key-to-sink map on the managed side. Reaching for a managed component to dodge that
is the wrong answer.

## Known limits of UI Toolkit itself

Verified against the installed 6000.3.9f1 assembly, not assumed — worth knowing before you
plan a screen around something that does not exist:

- **No per-element material or shader.** `VisualElement.materialOverride` is absent. An
  effect that needs a custom material on a UI element has no UI Toolkit equivalent.
- **`VisualElement` is plain managed C#**, not a `UnityEngine.Object`. It cannot be a
  Timeline or Animator binding target, and it cannot be touched from Burst or from a job.
  Animate with USS transitions or `schedule`.
- Present and usable in this version: `SetBinding`/`dataSource`, `PanelInputConfiguration`
  and `panelInputRedirection`, and world-space panels via `renderMode`/`worldSpaceSize`.
