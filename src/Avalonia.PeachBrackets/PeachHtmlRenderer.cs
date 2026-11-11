using System.Diagnostics;
using Avalonia.Platform;
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
    /// <param name="cancellationToken">Checked between pipeline stages and on each resource request; PeachBrackets itself takes no token.</param>
    /// <param name="resources">How images, style sheets and fonts the document refers to are loaded; null is <see cref="HtmlResourceOptions.Default"/>.</param>
    /// <param name="media">
    /// <see cref="MediaType.Print"/> (default) paginates onto <paramref name="page"/>; <see cref="MediaType.Screen"/> lays the document out as
    /// one continuous page of the sheet's width, and applies <c>@media screen</c>.
    /// </param>
    public async Task<RenderedHtmlPage> RenderPageAsync(
        string html,
        HtmlPageSettings page,
        int pageIndex = 0,
        double pixelsPerPoint = 96.0 / 72.0,
        string? userStyleSheet = null,
        CancellationToken cancellationToken = default,
        HtmlResourceOptions? resources = null,
        MediaType media = MediaType.Print)
    {
        ArgumentNullException.ThrowIfNull(html);
        if (!(pixelsPerPoint > 0) || double.IsInfinity(pixelsPerPoint))
            throw new ArgumentOutOfRangeException(nameof(pixelsPerPoint));

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var sw = Stopwatch.StartNew();
            using var document = await LoadAndLayoutAsync(html, page, userStyleSheet, resources, media, pixelsPerPoint, cancellationToken).ConfigureAwait(false);
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
        double pixelsPerPoint = 96.0 / 72.0, string? userStyleSheet = null, CancellationToken cancellationToken = default,
        HtmlResourceOptions? resources = null, MediaType media = MediaType.Print)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(output);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var document = await LoadAndLayoutAsync(html, page, userStyleSheet, resources, media, pixelsPerPoint, cancellationToken).ConfigureAwait(false);
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

    private async Task<HtmlDocument> LoadAndLayoutAsync(
        string html, HtmlPageSettings page, string? userStyleSheet, HtmlResourceOptions? resources, MediaType media,
        double pixelsPerPoint, CancellationToken ct)
    {
        // The context is shared and renders are serialized by _gate, so the per-render loader is swapped in here.
        // Images are requested during layout and paint, both of which run while the gate is held.
        var res = resources ?? HtmlResourceOptions.Default;
        Context.ResourceLoader = uri => LoadResourceAsync(uri, res, ct);
        html = HtmlBaseUriRewriter.Rewrite(html, res.BaseUri);

        var options = new HtmlDocumentOptions
        {
            PageWidthPoints = page.WidthPoints,
            PageHeightPoints = page.HeightPoints,
            MarginLeftPoints = page.MarginLeft,
            MarginTopPoints = page.MarginTop,
            MarginRightPoints = page.MarginRight,
            MarginBottomPoints = page.MarginBottom,
            // 0.9.26: MediaType.Screen means "one continuous surface, no pages"; Print paginates.
            MediaType = media,
            // CSS px per device px, for resolution / device-pixel-ratio media queries and srcset.
            DevicePixelRatio = pixelsPerPoint * 72.0 / 96.0,
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

    private static async ValueTask<RNetworkResponse?> LoadResourceAsync(RUri uri, HtmlResourceOptions options, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return null;

        // Without a <base href> (and RasterRenderContext.BaseUri is always null) the engine hands over relative addresses as-is.
        if (!uri.IsAbsoluteUri)
        {
            if (options.BaseUri is not { IsAbsoluteUri: true } baseUri)
                return null;
            if (!Uri.TryCreate(baseUri, uri.OriginalString, out var absolute))
                return null;
            uri = new RUri(absolute);
        }

        if (uri.Scheme == "avares")
        {
            if (!options.AllowAvaloniaResources)
                return null;
            try
            {
                var asset = AssetLoader.Open(uri.Uri);
                return new RNetworkResponse(asset, ContentTypeHeaders(uri.Uri.AbsolutePath));
            }
            catch (Exception e) when (e is FileNotFoundException or IOException or InvalidOperationException or NullReferenceException)
            {
                return null;
            }
        }

        try
        {
            var response = await HostResourceLoader.LoadAsync(uri, options.AllowNetwork, options.AllowLocalFiles).ConfigureAwait(false);
            // HostResourceLoader serves file: with no headers, but the engine only accepts a style sheet whose Content-Type is text/css.
            if (response is { ResponseHeaders: null } && uri.IsFile)
                response = response with { ResponseHeaders = ContentTypeHeaders(uri.Uri.LocalPath) };
            return response;
        }
        catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException or UriFormatException)
        {
            return null;
        }
    }

    private static Dictionary<string, string[]>? ContentTypeHeaders(string path)
    {
        var type = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".css" => "text/css",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".avif" => "image/avif",
            ".ttf" => "font/ttf",
            ".otf" => "font/otf",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".html" or ".htm" => "text/html",
            _ => null,
        };
        return type is null ? null : new Dictionary<string, string[]> { ["Content-Type"] = [type] };
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
