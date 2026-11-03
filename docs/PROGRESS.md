# Milestone 1 progress log

## Step 1 - Solution structure (done)
- `Avalonia.PeachBrackets.slnx` (SDK 10 solution format), `global.json` pins SDK 10.0.403 (rollForward latestFeature).
- `src/Avalonia.PeachBrackets` - control library, multi-targets net8.0 + net10.0, trimmable/AOT flags on.
- `samples/Avalonia.PeachBrackets.Demo` - demo app (net10.0).
- `tests/` reserved for tests.
- `Directory.Build.props` (nullable, implicit usings) and `Directory.Packages.props` (central package management).
- `dotnet build`: 0 warnings, 0 errors.

## Step 2 - NuGet references (done)
- Central pins in `Directory.Packages.props` (exact `[x]` ranges): PeachBrackets / PeachDrawing / PeachDrawing.Core `[0.9.25]`
  (latest stable; 0.9.25-preview1 and 0.9.24 also exist), Avalonia / Avalonia.Desktop / Themes.Fluent / Fonts.Inter /
  Headless / Headless.XUnit / Skia `[12.1.5]`, test stack Microsoft.NET.Test.Sdk `[18.11.0]`, xunit.v3 `[3.2.2]`,
  xunit.runner.visualstudio `[3.1.5]`.
- PeachBrackets 0.9.25 transitively brings PeachDrawing.Text 0.9.25, PeachImage 0.6.2, MimeKit 4.18.2,
  System.Security.Cryptography.Pkcs 10.0.14 (net8.0 also System.IO.Pipelines).
- `dotnet build`: 0 warnings, 0 errors.

## Step 3 - POC control (done)
- `PeachHtmlRenderer` (UI-independent): `HtmlDocument.LoadAsync` -> `LayoutAsync(measuring canvas)` -> `PaintPageAsync`
  onto a `RasterCanvas` built over a `RasterSurface` scaled to `pixelsPerPoint`; returns premultiplied RGBA8 in `RenderedHtmlPage`.
  Load + layout run on the thread pool; renders on one renderer are serialized (shared `RasterRenderContext`, documents not thread safe).
- `HtmlView : Control` - styled properties `Html`, `Source`, `PageWidth`, `PageHeight`, `PageMargin`, `PageIndex`,
  `RenderScale`, `PageBackground`, `UserStyleSheet`; direct read-only `PageCount`, `IsRendering`, `RenderError`,
  `DocumentTitle`; `RenderCompleted` event. Changes coalesce into one render per dispatcher turn; stale renders are cancelled
  and their results dropped. The page is copied into a `WriteableBitmap` (Rgba8888, Premul, DPI = 72 x pixels-per-point) and
  drawn in `Render`.
- Tests (`tests/Avalonia.PeachBrackets.Tests`, xunit.v3 + Avalonia.Headless.XUnit): 6/6 pass.
- API notes found while building (see README "API notes"): `RasterRenderContext` is sealed and its `LayoutUnitsPerPoint` and
  `BaseUri` are get-only; `CreateCanvas` is fixed at 1 unit = 1 px, so HiDPI uses `new RasterSurface(...)` + `new RasterCanvas(ctx, surface, 1.0)`;
  `RasterRenderContext` does not override `GetResourceStream`, so `<img>` (even `data:` URIs), `<link>` stylesheets and `@font-face url()` don't load.

## Step 4 - Demo window (done)
- `samples/Avalonia.PeachBrackets.Demo`: toolbar (page size A4/Letter/A5/A4 landscape, margin in pt, page number + count,
  render status), live HTML editor on the left, `HtmlView` on a grey "desk" on the right. The view starts from
  `Source="avares://Avalonia.PeachBrackets.Demo/Assets/sample.html"`; typing in the editor sets `Html` (which wins over `Source`).
- Sample covers headings, a styled paragraph, inline-block tags, a zebra table, inline SVG (gradient, shapes, path, text),
  CSS colours (hex/rgb/hsl/gradient), MathML and a list.
- MathML rendered as tofu until a math font was registered: the demo ships Noto Sans Math (OFL 1.1) and registers it via
  `PeachHtmlRenderer.Shared.RegisterFontAsync`.
- Verified: `dotnet run --project samples/Avalonia.PeachBrackets.Demo -- --screenshot docs` (Avalonia.Headless + Skia) wrote
  `docs/demo-screenshot.png` (1400x960 window) and `docs/demo-page.png` (raw 794x1123 page bitmap at 96 DPI). The normal
  desktop (X11) path was also run under Xvfb: `docs/demo-xvfb-desktop.png`. First render ~1.1 s (includes font-set setup).
