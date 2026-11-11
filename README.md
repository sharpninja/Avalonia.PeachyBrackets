# Avalonia.PeachBrackets

Avalonia 12 controls that render HTML/CSS/SVG/MathML with [PeachBrackets](https://www.nuget.org/packages/PeachBrackets),
the pure-.NET engine behind [PeachPDF](https://github.com/jhaygood86/PeachPDF).

**Status: milestone 1 (proof of concept).** A read-only `HtmlView` lays a document out into pages and shows one page as a
bitmap. Interaction (selection, links, scrolling through pages) comes later.

![Demo window](docs/demo-screenshot.png)

## Layout

```
Avalonia.PeachBrackets.slnx
Directory.Build.props          shared settings (nullable, implicit usings)
Directory.Packages.props       central, exact package pins
global.json                    .NET SDK 10.0.403 (rollForward: latestFeature)
src/Avalonia.PeachBrackets/    the control library (net8.0; net10.0, trimmable/AOT-flagged)
samples/Avalonia.PeachBrackets.Demo/   demo app (net10.0)
tests/Avalonia.PeachBrackets.Tests/    xunit.v3 + Avalonia.Headless.XUnit
docs/                          progress log, screenshots
```

## Pinned versions

| Package | Version |
| --- | --- |
| PeachBrackets, PeachDrawing, PeachDrawing.Core | `[0.9.26]` (exact; pre-1.0, versioned in lockstep, bump together; latest on nuget.org as of 2026-11-11) |
| Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent, Avalonia.Fonts.Inter, Avalonia.Skia, Avalonia.Headless, Avalonia.Headless.XUnit | `[12.1.5]` |
| Microsoft.NET.Test.Sdk / xunit.v3 / xunit.runner.visualstudio | `[18.11.0]` / `[3.2.2]` / `[3.1.5]` |

PeachBrackets 0.9.26 pulls in PeachDrawing.Text / PeachDrawing.Text.Data 0.9.26, PeachImage 0.7.3, MimeKit 4.18.2,
BouncyCastle.Cryptography 2.7.0 and System.Security.Cryptography.Pkcs 10.0.14 transitively. (Upstream `main` has unreleased
commits past the `v0.9.26` tag; we stay on the published package.)

## Build, test, run

Needs the .NET 10 SDK (the library also targets net8.0).

```bash
dotnet build
dotnet test
dotnet run --project samples/Avalonia.PeachBrackets.Demo            # desktop window
dotnet run --project samples/Avalonia.PeachBrackets.Demo -- --screenshot docs   # headless: writes docs/demo-screenshot.png + docs/demo-page.png
```

The `--screenshot` mode uses Avalonia.Headless with Skia, so it needs no display. In the window, change the page size,
margin or page number in the toolbar, or edit the HTML on the left; the page re-renders.

## Usage

```xml
<Window xmlns:pb="clr-namespace:Avalonia.PeachBrackets;assembly=Avalonia.PeachBrackets">
  <ScrollViewer>
    <pb:HtmlView Source="avares://MyApp/Assets/page.html"
                 PageWidth="612" PageHeight="792" PageMargin="36" />
  </ScrollViewer>
</Window>
```

```csharp
view.Html = "<h1 style='color:teal'>Hello</h1><p>From PeachBrackets.</p>";
view.RenderCompleted += (_, e) => Console.WriteLine(e.Error?.Message ?? $"{e.Page!.PageCount} page(s)");

// Fonts that are not installed (and every font on WASM) must be registered before rendering:
await PeachHtmlRenderer.Shared.RegisterFontAsync(File.OpenRead("NotoSansMath-Regular.ttf"), "Noto Sans Math");

// Images / style sheets / fonts the document refers to: data:, file: and avares: load by default; http(s) is opt-in.
view.BaseUri = new Uri("file:///home/me/site/");   // relative src/href resolve here (Source is used when BaseUri is null)
view.AllowNetwork = true;                           // let the document fetch http(s) resources

// Without a control: render a page to pixels or a PNG.
var page = await PeachHtmlRenderer.Shared.RenderPageAsync(html, HtmlPageSettings.A4, pageIndex: 0, pixelsPerPoint: 2);
await PeachHtmlRenderer.Shared.RenderPageToPngAsync(html, HtmlPageSettings.Letter, File.Create("page1.png"));
```

## Public surface (0.1.0-alpha)

All lengths are **points** (1/72 inch), as in PeachBrackets. On screen 1 pt = 96/72 DIP.

### `HtmlView : Control`

| Member | Kind | Notes |
| --- | --- | --- |
| `Html` | `StyledProperty<string?>` | HTML source. Wins over `Source` when non-null. |
| `Source` | `StyledProperty<Uri?>` | `file:`, `http(s):` or `avares:`; read as text, used when `Html` is null. |
| `PageWidth`, `PageHeight` | `StyledProperty<double>` | Sheet size, default A4 595 x 842. A `@page { size }` rule overrides them. |
| `PageMargin` | `StyledProperty<Thickness>` | Default 20 on each side. `@page { margin }` overrides it. |
| `PageIndex` | `StyledProperty<int>` | Page to show, clamped to the last page. |
| `RenderScale` | `StyledProperty<double>` | 0 (default) = the window's `RenderScaling`, so HiDPI stays sharp; otherwise pixels per DIP. |
| `PageBackground` | `StyledProperty<IBrush?>` | Painted behind the page (default white); PeachBrackets leaves unpainted pixels transparent. |
| `UserStyleSheet` | `StyledProperty<string?>` | CSS applied before the document's own styles. |
| `BaseUri` | `StyledProperty<Uri?>` | Base for relative `src`/`href`/`url()` when the document has no `<base href>`. Null + `Source` set = `Source`. |
| `AllowNetwork` | `StyledProperty<bool>` | Opt-in (default false): fetch `http(s)` images, style sheets and fonts. Does not affect loading `Source` itself. |
| `AllowLocalFiles` | `StyledProperty<bool>` | Read `file:` references (default true). `data:` and `avares:` always load. |
| `MediaType` | `StyledProperty<MediaType>` | PeachBrackets `MediaType.Print` (default, paginated) or `MediaType.Screen` (one continuous page of `PageWidth`, `@media screen`). |
| `PageCount`, `IsRendering`, `RenderError`, `DocumentTitle` | read-only `DirectProperty` | State of the last render. |
| `CurrentBitmap` | `Bitmap?` | The page bitmap being shown. |
| `Renderer` | `PeachHtmlRenderer` | Defaults to `PeachHtmlRenderer.Shared`. |
| `RenderCompleted` | event | UI thread, after each render that was not superseded; carries `Page` or `Error`. |
| `InvalidateDocument()` | method | Force a re-render. |
| `ToWriteableBitmap(RenderedHtmlPage)` | static | Premultiplied RGBA8 -> `WriteableBitmap` (Rgba8888, Premul). |

Behaviour: property changes made in one dispatcher turn coalesce into one render. A new render cancels the one in flight
and the older result is dropped. Load and layout run on the thread pool. The control's desired size is the page size.

### `PeachHtmlRenderer`

UI-independent pipeline wrapper. It holds one `RasterRenderContext` (font, brush and pen caches; installed fonts are
registered when it is created) and serializes renders, because PeachBrackets documents are not thread safe and the
context is shared.

- `static Shared`, `Context`, `RegisterFontAsync(Stream, family)`
- `RenderPageAsync(html, HtmlPageSettings, pageIndex, pixelsPerPoint, userStyleSheet, ct, HtmlResourceOptions? resources, MediaType media)` -> `RenderedHtmlPage`
- `RenderPageToPngAsync(html, HtmlPageSettings, Stream, ...)`

### `HtmlResourceOptions`

`record HtmlResourceOptions(Uri? BaseUri, bool AllowNetwork = false, bool AllowLocalFiles = true, bool AllowAvaloniaResources = true)`,
with `Default` (data:, file:, avares:; no network) and `DataOnly`. The renderer sets `RasterRenderContext.ResourceLoader` for each
render: relative addresses are resolved against `BaseUri`, `avares:` goes through `AssetLoader`, and everything else goes through
PeachBrackets' `HostResourceLoader.LoadAsync(uri, allowNetwork, allowLocalFiles)`.

### `HtmlPageSettings`, `RenderedHtmlPage`, `HtmlRenderCompletedEventArgs`

`HtmlPageSettings(WidthPoints, HeightPoints, MarginLeft, MarginTop, MarginRight, MarginBottom)` provides `A4`, `Letter`
and `WithUniformMargin`. `RenderedHtmlPage` holds `PageIndex`, `PageCount`, `WidthPoints`, `HeightPoints`,
`PixelsPerPoint`, `PixelWidth`, `PixelHeight`, `PremultipliedRgba`, `Title` and `Elapsed`.

## How it works

```
ctx.ResourceLoader = uri => (avares: ? AssetLoader : HostResourceLoader.LoadAsync(uri, allowNetwork, allowLocalFiles))
HtmlDocument.LoadAsync(html', RasterRenderContext, HtmlDocumentOptions{page size, margins, MediaType, DevicePixelRatio})
                                                    // html' = <link href> made absolute, <base href> injected (see note 2)
  -> LayoutAsync(ctx.CreateCanvas(1, 1))            // measuring only
  -> PaintPageAsync(i, new RasterCanvas(ctx, new RasterSurface(wPx, hPx, 0, 0, ppp, ppp), 1.0))
  -> RasterCanvas.ToPixelBuffer()                   // premultiplied RGBA8, copied out
  -> WriteableBitmap(Rgba8888, Premul, dpi = 72 * ppp) -> DrawingContext.DrawImage
```

## API notes and blockers (PeachBrackets / PeachDrawing 0.9.26)

The pipeline names matched: `HtmlDocument.LoadAsync`, `LayoutAsync`, `PaintPageAsync`, `Pages` (`DocumentPage` with
`Index`, `WidthPoints`, `HeightPoints`), `RasterRenderContext`, `RasterCanvas`, `ToPixelBuffer`.

**API change hit going 0.9.25 -> 0.9.26:** `HtmlDocumentOptions.Media` (string) was replaced by `MediaType MediaType`
(`Print`/`Screen`), and `Screen` now also means "one continuous surface, no pages". The renderer defaults to `Print` (paginated,
as before) and exposes `MediaType`. Other 0.9.26 changes (`Task` -> `ValueTask` on `RenderContext` font/resource methods,
`AddFont` returning `ValueTask`) needed no code changes.

1. **HiDPI scaling (still open, workaround kept).** `RasterRenderContext` is `sealed`, `LayoutUnitsPerPoint` is get-only (1),
   and `CreateCanvas(w, h, dpi)` still ignores `dpi`. **Workaround:** keep layout in points and paint onto
   `new RasterCanvas(ctx, new RasterSurface(wPx, hPx, 0, 0, scale, scale), 1.0)`. 0.9.26's `HtmlDocumentOptions.DevicePixelRatio`
   is now set too, so `resolution` media queries and `srcset` see the real density.
2. **Resource loading (fixed in 0.9.26, with two workarounds).** `RasterRenderContext.ResourceLoader` (new) plus the public
   `HostResourceLoader` now load `<img>` (incl. `data:`), `<link rel=stylesheet>`, CSS `url()` and `@font-face`. We wire them up
   with `HtmlResourceOptions` / `AllowNetwork`. Remaining engine gaps we work around:
   - `RasterRenderContext.BaseUri` is still always null and `<link>` sheets load before `<base href>` is seen; a relative `<link href>`
     then throws "This operation is not supported for a relative URI" and fails the whole render. `HtmlBaseUriRewriter` makes
     `<link href>` absolute (or an empty `data:text/css,` sheet when there is no base) and injects `<base href>` so images and CSS
     `url()` resolve during layout.
   - `HostResourceLoader` serves `file:` with no `Content-Type`, but the engine only accepts a style sheet whose type is `text/css`;
     we add a `Content-Type` by file extension (also for `avares:`).
   - Still unresolved: relative `@font-face src: url(...)` / `@import` inside an inline `<style>` (no base for the sheet); use absolute or
     `data:` URLs there.
3. **Transparent page.** Unpainted pixels are alpha 0. `HtmlView.PageBackground` (white) is painted underneath.
4. **No cancellation inside the engine (still open).** `LoadAsync`, `LayoutAsync` and `PaintPageAsync` still take no
   `CancellationToken`. Stale renders are cancelled between stages and on each resource request, and their results thrown away.
5. **Inline MathML baseline.** The inline `<math>` in the sample sits above the text baseline. This is engine behaviour,
   not something the control does.

## Known limitations

- **Page-oriented by default.** With `MediaType.Print` the view shows one page at a time at a fixed sheet size.
  `MediaType.Screen` gives one continuous page of `PageWidth`, but the view doesn't yet reflow to the control's width.
- **Pre-1.0 API.** PeachBrackets/PeachDrawing 0.9.x may change between releases, so the pins are exact. Expect
  adjustments on every bump.
- **Software rasterizer.** Pages are painted on the CPU and uploaded as a bitmap. Every change re-runs load, layout and
  paint for the whole document (the first render took about 1.1 s for the sample, including font-set initialisation).
  Zooming past `RenderScale` blurs.
- **Fonts.** `RasterRenderContext` registers the machine's installed fonts (about 160 ms at startup on the dev box). WASM,
  minimal containers and fonts shipped as assets need `RegisterFontAsync`. MathML needs a math font (the demo bundles
  Noto Sans Math, OFL). Without one, math identifiers render as tofu boxes.
- **External resources** load (see API note 2); network is opt-in per view (`AllowNetwork`). `HostResourceLoader`
  keeps a process-wide HTTP cache and user agent.
- **No interaction yet.** No selection, links, hover, forms or scrolling between pages.
- **One shared context.** Renders on a renderer are serialized. Create separate `PeachHtmlRenderer` instances for parallel
  rendering; each one costs a font-set scan.

## Next steps

1. **Multi-page and virtualization.** A `HtmlDocumentView` that keeps the laid-out `HtmlDocument` alive, stacks pages
   in a virtualizing panel, and paints only the visible pages, caching or evicting bitmaps by viewport.
   Reuse layout across repaints instead of reloading.
2. **Continuous layout.** Set the sheet width to the control width and the height to "tall", re-layout on resize, and
   suppress page breaks, so the view behaves like a browser viewport.
3. **Selection and hit-testing.** Turn on `RecordTextRuns` and `EnableSelectionStyling`. Map pointer events through
   `HitTest`, `GetCaretPosition` and `Selection.SelectAt` / `ExtendTo` / `SelectWordAt`, repaint `DirtyRegions` with
   `PaintPageRegionAsync`, and copy with `GetText`. Add find (`FindText`, `SetHighlight`).
4. **Links.** Use `Annotations.Links` for hover cursors and a `LinkClicked` event or command; navigate in-document anchors
   via `Destination`.
5. **Resource loading upstream fixes.** Ask upstream for a settable base URI on `RasterRenderContext` (or `HtmlDocumentOptions`),
   for `<link>` resolution that honours `<base>`, and for a `Content-Type` on `file:` responses, then drop `HtmlBaseUriRewriter`.
6. **Custom Canvas -> DrawingContext.** Implement `PeachDrawing.Core.Canvas` over Avalonia's `DrawingContext` (or a
   Skia-backed one) for GPU, resolution-independent painting. This needs a full mapping of brushes, paths, clips, blend
   modes, glyph runs and layers. Keep the raster path as the fallback.
7. **Forms and incremental updates.** `CollectFormFields` with overlaid Avalonia input controls; `SetStyle` and
   `ReplaceTextAsync` for live edits without a full reload.
8. **Packaging.** Pick a licence, add a NuGet package, test trimming/AOT (`PublishAot`) and an Avalonia.Browser (WASM)
   sample with bundled fonts.

See [docs/PROGRESS.md](docs/PROGRESS.md) for the per-step log.
