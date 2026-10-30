# ConnectorOutput: a stand-in for a real import

**Nothing in this folder came from Figma.** It is a small hand-made fixture with the shape the
UI Toolkit Connector for Figma (`com.unity.ui.figma` 0.3.0-exp.1) writes for one frame,
`ShopCard`, in a Figma file named "Cuvara Shop". It lets the sample run, and the validator
check it, without the connector, a Figma account or a network.

In a game you do not create these files. The connector writes them to
`Assets/Figma/<file>/` (here that would be `Assets/Figma/Cuvara-Shop/`) on every push, and
rewrites every one of them on the next push. Never edit them by hand, and never let a tool of
yours write into that folder.

| File | What the connector writes it for |
|---|---|
| `ShopCard.uxml`, `ShopCard.uss` | the frame. Links `tokens.uss`, the mode it resolved to (`light.uss`) and its own sheet. Plain layers carry their whole style inline; bound values are `var(--token)` |
| `PriceTag.uxml`, `PriceTag.uss` | the "Price Tag" component, used through `<Template>` / `<ui:Instance>`. Its text layers are named `text-0`, `text-1` for `AttributeOverrides` |
| `tokens.uss` | every COLOR and FLOAT variable on `:root` |
| `light.uss`, `dark.uss` | one `:root` sheet per variable mode |
| `images/5e1a7c.png` | a rasterised image fill, imported as a plain texture |
| `.figma-manifest.json` | the connector's import record: screens, generated files, tokens |

Differences from a real import, all deliberate:

- `ShopCard.uxml` carries one XML comment that marks it as a stand-in. A real file has none.
- The image `url()` is relative: `url("images/5e1a7c.png")`. The connector writes an absolute
  one, `url("project://database/Assets/Figma/Cuvara-Shop/images/5e1a7c.png?fileID=2800000&guid=...&type=3#5e1a7c")`.
  An absolute path cannot be right for a sample, because the Package Manager imports it under
  a versioned folder; the validator would report it as `FIG040`.
- `.figma-manifest.json` has no `.meta`: Unity does not import dot-files.
- This `README.md` would be a foreign file in a real import folder.

Validate it the way a game's CI validates its committed import (see
`Documentation~/FIGMA-VALIDATION.md`):

```bash
dotnet run --project Packages/com.cuvara.uitoolkit/Tools~/UxmlCodegenCli/UxmlCodegenCli.csproj -- \
  validate "Assets/Samples/Cuvara UI Toolkit/<version>/Figma Import/ConnectorOutput" --prefix shop
```

Expected: 0 errors, 0 warnings, 3 infos (`FIG021` colour summary, `FIG031` inline-style counts).
