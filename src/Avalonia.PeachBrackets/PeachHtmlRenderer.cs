using System.Diagnostics;
using PeachBrackets;
using PeachDrawing;
using PeachDrawing.Core;
using PeachImage.Formats.Png;

namespace Avalonia.PeachBrackets;

/// <summary>
/// UI-independent wrapper over the PeachBrackets pipeline
/// (<c>HtmlDocument.LoadAsync</c> -> <c>LayoutAsync</c> -> <c>PaintPageAsync</c>) that rasterizes one page
/// with PeachDrawing's CPU <see cref="RasterCanvas"/>.
/// </summary>
/// <remarks>
/// A <see cref="RasterRenderContext"/> caches fonts, brushes and pens and registers every installed font when it is
/// constructed, so one renderer (and one context) should be shared; <see cref="Shared"/> is that instance.
/// PeachBrackets documents are not thread safe, and the context is shared, so renders on one renderer are serialized.
/// </remarks>
public sealed class PeachHtmlRenderer
{
    private static readonly Lazy<PeachHtmlRenderer> s_shared = new(() => new PeachHtmlRenderer());
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Creates a renderer with its own <see cref="RasterRenderContext"/> (installed fonts pre-registered).</summary>
    public PeachHtmlRenderer() : this(new RasterRenderContext()) { }

    /// <summary>Creates a renderer over an existing context.</summary>
    public PeachHtmlRenderer(RasterRenderContext context) =>
        Context = context ?? throw new ArgumentNullException(nameof(context));

    /// <summary>The process-wide renderer that <see cref="HtmlView"/> uses unless given another.</summary>
    public static PeachHtmlRenderer Shared => s_shared.Value;

    /// <summary>The PeachDrawing context fonts are registered on.</summary>
    public RasterRenderContext Context { get; }

    /// <summary>
    /// Registers a TrueType/OpenType/WOFF/WOFF2 font under <paramref name="familyName"/>, so a document's
    /// <c>font-family</c> can name it. Needed on platforms with no installed fonts (WASM, minimal containers)
    /// and for fonts shipped as app assets. Register before rendering documents that use it.
    /// </summary>
    public async Task RegisterFontAsync(Stream fontStream, string familyName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fontStream);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Context.AddFont(fontStream, familyName).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Loads <paramref name="html"/>, paginates it onto <paramref name="page"/> and paints page
    /// <paramref name="pageIndex"/> at <paramref name="pixelsPerPoint"/> device pixels per point.
    /// </summary>
    /// <param name="html">The HTML source.</param>
    /// <param name="page">Sheet size and margins, in points.</param>
    /// <param name="pageIndex">Zero-based page to paint; clamped to the last page.</param>
    /// <param name="pixelsPerPoint">Raster scale. 96/72 (~1.333) matches a 96-DPI screen at 100%.</param>
    /// <param name="userStyleSheet">Optional CSS applied before the document's own styles.</param>
    /// <param name="cancellationToken">Checked between pipeline stages; PeachBrackets itself takes no token.</param>
    public async Task<RenderedHtmlPage> RenderPageAsync(
        string html,
        HtmlPageSettings page,
        int pageIndex = 0,
        double pixelsPerPoint = 96.0 / 72.0,
        string? userStyleSheet = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(html);
        if (!(pixelsPerPoint > 0) || double.IsInfinity(pixelsPerPoint))
            throw new ArgumentOutOfRangeException(nameof(pixelsPerPoint));

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var sw = Stopwatch.StartNew();
            using var document = await LoadAndLayoutAsync(html, page, userStyleSheet, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var pageCount = document.Pages.Count;
            if (pageCount == 0)
                return new RenderedHtmlPage(0, 0, page.WidthPoints, page.HeightPoints, pixelsPerPoint, 0, 0, [], document.Metadata?.Title, sw.Elapsed);

            var index = Math.Clamp(pageIndex, 0, pageCount - 1);
            var sheet = document.Pages[index];
            using var canvas = CreatePageCanvas(sheet, pixelsPerPoint, out var widthPx, out var heightPx);
            await document.PaintPageAsync(index, canvas).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            // ToPixelBuffer wraps the surface's own array without copying; copy before the canvas is disposed.
            var pixels = canvas.ToPixelBuffer().PremultipliedRgba.ToArray();
            return new RenderedHtmlPage(index, pageCount, sheet.WidthPoints, sheet.HeightPoints, pixelsPerPoint,
                widthPx, heightPx, pixels, document.Metadata?.Title, sw.Elapsed);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Renders one page and writes it to <paramref name="output"/> as a PNG (transparent where unpainted).</summary>
    public async Task RenderPageToPngAsync(
        string html, HtmlPageSettings page, Stream output, int pageIndex = 0,
        double pixelsPerPoint = 96.0 / 72.0, string? userStyleSheet = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(output);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var document = await LoadAndLayoutAsync(html, page, userStyleSheet, cancellationToken).ConfigureAwait(false);
            if (document.Pages.Count == 0) throw new InvalidOperationException("The document produced no pages.");
            var index = Math.Clamp(pageIndex, 0, document.Pages.Count - 1);
            using var canvas = CreatePageCanvas(document.Pages[index], pixelsPerPoint, out _, out _);
            await document.PaintPageAsync(index, canvas).ConfigureAwait(false);
            await canvas.SaveAsync(output, "png", new PngEncoderOptions(), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<HtmlDocument> LoadAndLayoutAsync(string html, HtmlPageSettings page, string? userStyleSheet, CancellationToken ct)
    {
        var options = new HtmlDocumentOptions
        {
            PageWidthPoints = page.WidthPoints,
            PageHeightPoints = page.HeightPoints,
            MarginLeftPoints = page.MarginLeft,
            MarginTopPoints = page.MarginTop,
            MarginRightPoints = page.MarginRight,
            MarginBottomPoints = page.MarginBottom,
            // A viewer is a screen; PeachBrackets defaults to "print".
            Media = "screen",
            UserStyleSheet = userStyleSheet,
        };

        // Load + layout are CPU-bound; keep them off the caller's (UI) thread.
        return await Task.Run(async () =>
        {
            ct.ThrowIfCancellationRequested();
            var document = await HtmlDocument.LoadAsync(html, Context, options).ConfigureAwait(false);
            try
            {
                ct.ThrowIfCancellationRequested();
                // Layout only measures text on this canvas; RasterRenderContext.LayoutUnitsPerPoint is fixed at 1,
                // so layout units are points.
                using var measuring = Context.CreateCanvas(1, 1);
                await document.LayoutAsync(measuring).ConfigureAwait(false);
                return document;
            }
            catch
            {
                document.Dispose();
                throw;
            }
        }, ct).ConfigureAwait(false);
    }

    private RasterCanvas CreatePageCanvas(DocumentPage sheet, double pixelsPerPoint, out int widthPx, out int heightPx)
    {
        // RasterRenderContext.CreateCanvas is always 1 unit = 1 pixel, and the context's LayoutUnitsPerPoint is
        // read-only (1). To get a sharp bitmap we build the surface ourselves: layout units stay points and the
        // surface maps each unit to pixelsPerPoint device pixels.
        widthPx = Math.Max(1, (int)Math.Ceiling(sheet.WidthPoints * pixelsPerPoint));
        heightPx = Math.Max(1, (int)Math.Ceiling(sheet.HeightPoints * pixelsPerPoint));
        var surface = new RasterSurface(widthPx, heightPx, 0, 0, pixelsPerPoint, pixelsPerPoint);
        return new RasterCanvas(Context, surface, 1.0);
    }
}
