# Sliced art importer and UI import checker

Two Editor tools in the `Cuvara.UIToolkit.Editor` assembly. Both came from penpot-uitoolkit-bridge
([PENPOT-BRIDGE-MOVE.md](PENPOT-BRIDGE-MOVE.md)); neither contains anything specific to Penpot.

## Sliced art importer (`Cuvara.UIToolkit.Editor.SlicedArt`)

`SlicedArtImporter` (an `AssetPostprocessor`) imports a PNG that has a sibling `<name>.slice.json` as a 9-slice Sprite:

```json
{"version":1,"border":[left,top,right,bottom],"pixelsPerUnit":100,"maxSize":2048,"group":"panels"}
```

Applied settings: Sprite, Single, `spriteBorder = (left, bottom, right, top)` (Unity order; the sidecar order is left, top,
right, bottom), `alphaIsTransparency`, no mipmaps, Bilinear, Clamp, NPOT scale None, uncompressed (block compression adds visible
error on 1 px ornaments), `maxTextureSize` and `pixelsPerUnit` from the sidecar. Border values must be `>= 0`; a missing or
non-positive `pixelsPerUnit` / `maxSize` falls back to 100 / 2048. When a sidecar is imported, changed or moved, its PNG is
force-reimported. PNGs without a sidecar are never touched. `SliceSidecar` reads and validates the file
(`SliceSidecar.TryLoad`, `SidecarPathFor`, `PngPathFor`, `ToSpriteBorder`).

### It is off by default

Two importers that act on the same `.slice.json` must never run in one project: whichever runs last wins, silently. A project
that still uses the penpot-uitoolkit-bridge importer (`PenpotArtImporter`) must keep this one off. The bridge will switch to this
importer in a later release and drop its own; until then, enable only one of them.

Enable it with `ProjectSettings/CuvaraSlicedArtSettings.json` (committed, the same for every machine and batch run):

```json
{"enabled":true,"roots":["Assets/UI/Art/"]}
```

Only PNGs under one of `roots` are touched; `enabled` with no roots touches nothing. The settings are read once per domain
reload; after changing the file, reimport the art folders so existing PNGs pick up the change. `SlicedArtSettings.SetEnabled`,
`SetRoots` and `Reload` override the file in memory (tests use them).

## UI import checker (`Cuvara.UIToolkit.Editor.ImportCheck`)

```csharp
UiImportCheckResult UiImportChecker.Check(string[] assetPaths);
string UiImportChecker.CheckToJson(string commaSeparatedPaths); // batch mode, menus, Unity MCP script execution
```

Accepts `.uxml`, `.uss` and `.png` paths; a folder expands to all of them. It also follows every stylesheet, template and sprite
reachable from the given assets (`<Style src>`, `<Template src>`, `url()`, `@import`), so a UXML cannot pass on a stale
dependency. While it force-reimports each asset it captures every Unity log message; then it loads USS as `StyleSheet`
(`importedWithErrors` / `importedWithWarnings` fail), loads UXML as `VisualTreeAsset` and clones it, checks that every reference
resolves to an existing asset, and compares the border of every sprite that has a `.slice.json` with the sidecar (an unsliced
`[0,0,0,0]` sidecar matches an unsliced sprite). Strict: any Warning, Error, Assert or Exception logged during the check fails it.

```json
{"pass":false,"errors":[],"warnings":[],"logs":[],"checked":3}
```

The checker is on demand only (no menu item, no postprocessor), so it can run next to the bridge's `PenpotImportChecker`. It
reads sidecars whatever the importer switch says; a sliced sprite that no importer configured fails with
`textureType is Default, expected Sprite`.

## Tests

`Cuvara.UIToolkit.Editor.Tests`: `SlicedArtImporterTests` (7), `SliceSidecarContractTests` (1, reads
`Tests/Editor/SlicedArt/Fixtures/contract.slice.json`, byte-identical to the bridge converter's contract fixture) and
`UiImportCheckerTests` (9). They create temporary folders under `Assets/CuvaraUIToolkitTests_*` and enable the importer in memory
for those folders only.
