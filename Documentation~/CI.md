# What CI checks, and what it cannot

## The package is standalone, and that is what makes CI possible

An earlier draft of this package extended GameFoundation: every runtime file referenced it,
and GameFoundation lives in a private repository this org's Actions cannot fetch. A package
in that shape **cannot be compiled or tested by its own CI at all** — the job dies at package
resolution, before a line of the package is read, and it dies that way forever.

Severing that dependency is what bought the jobs below. It is worth stating plainly, because
the cost of the severing was real and this is the return on it.

## What runs

All jobs are in `.github/workflows/ci.yml`. The Unity jobs run on 6000.3.9f1.

| Job | What it proves | Gates? |
|---|---|---|
| `Validate package` | `package.json` shape, no unresolvable dependency declared, **the standalone gate**, the `cuvara-` USS prefix, CHANGELOG currency, `.meta` coverage, declared samples resolve their elements, asmdef names, and the Figma validator CLI on its fixtures (the good fixture exits 0; the bad one exits 1 with exactly 7 errors and 8 warnings, see [FIGMA-VALIDATION.md](FIGMA-VALIDATION.md#fixtures-and-ci)) | yes |
| `Compile samples` | every sample `package.json` declares, copied under `Assets/` with `com.unity.entities`, compiles | yes |
| `Unity Tests` | the package compiles and its EditMode and PlayMode tests pass in a project built from its own declarations, with `com.unity.entities` so the ECS tests run | yes |
| `Figma connector tests` | the same project plus `com.unity.ui.figma` `0.3.0-exp.1`, so the gated `Cuvara.UIToolkit.Editor.Figma` assembly compiles and its tests run | yes |
| `Install probe (documented)` | a consumer following the README can compile it | yes |
| `Install probe (bare)` | what a consumer sees with no scoped registry | no — informational, expected to fail |

Test counts are not written down here, because they change with every test added. Read them in
the `Assert tests ran and passed` step summary of a run (`total=… passed=…`), or in the uploaded
`test-results-*` artifacts.

## The three checks that exist because something got through

**The standalone gate** (`.github/scripts/check_standalone.py`) fails on any reference to
GameFoundation or its logging and resource assemblies under `Runtime/` or `Tests/`, by symbol,
by namespace, and by substring.

The substring list was added after the gate passed a `Runtime/` that still shipped the USS
class names `gdk-grid-row`, `gdk-grid-cell` and `gdk-multi-template-shell` — `gdk` being
GameDevelopmentKit, the framework this package came out of. They are string literals, so no
symbol and no namespace matched, and a green gate reported the severing complete while a
consumer's stylesheet would have been written against the previous vendor's prefix
permanently. A person caught it; the script did not. That is the whole reason the third list
exists.

**The `.meta` gate.** A missing `.meta` does not fail a build here and shows in no test result
— it fails a *consumer's* whole suite, because Unity logs an Error and the test framework
turns an unexpected log error into an exception. This package's first tranche shipped five
assets with no `.meta` at all and fifteen more carrying only `fileFormatVersion` and `guid`.

**The assertion that tests actually ran.** The runner is started with `USE_EXIT_CODE=false`,
so Unity exiting 0 means only that Unity started and stopped. It exits 0 on *"No tests were
executed"* as well, and the published check goes neutral rather than red. A repository can run
green for its entire history while executing nothing. The gate parses the result XML and fails
on `total == 0`. The `Figma connector tests` job also fails when no
`Cuvara.UIToolkit.Editor.Figma.Tests` case ran: that means the `CUVARA_UITOOLKIT_FIGMA` define
did not take effect and the gated assembly was never compiled.

## What CI still does not prove

Stated because a green run is easy to over-read:

- **No device, no rendering.** No screenshot, no Game View, no notched screen. Every safe-area
  test injects the rect it is given; nothing verifies what a real device reports.
- **No real input.** The back-navigation tests synthesise `NavigationCancelEvent`. That a real
  Android back press, a gamepad B, or Escape produces one at the panel root is Unity dispatch
  behaviour and is untested here.
- **No IL2CPP and no stripping.** Nothing is built for Android or WebGL.
- **Samples are compiled, not run.** The `Compile samples` job copies every declared sample
  under `Assets/` (Unity does not import `Samples~` itself) and compiles it. No sample scene is
  played.
- **No real Figma import.** The Figma jobs test the integration against fixtures and the
  connector's assembly, not against a push from a Figma file.
- **No UXML drift check.** This workflow does not run `Tools~/UxmlCodegenCli` as a drift check
  over the package's enrolled UXML ([UXML-CODEGEN.md](UXML-CODEGEN.md)). Run it locally.

## A note on the bare probe

`Install probe (bare)` is expected to fail. Its compile step carries `continue-on-error`, and
only the `documented` row fails the build when compilation fails. There is deliberately no
job-level `continue-on-error`: at job level it reports **success** whatever happens inside,
which made an earlier green tick there mean nothing. Read the bare row's summary, not its
status.
