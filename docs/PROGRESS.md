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
