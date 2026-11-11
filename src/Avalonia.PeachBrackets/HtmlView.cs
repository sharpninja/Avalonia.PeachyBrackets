using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using PeachBrackets;

namespace Avalonia.PeachBrackets;

/// <summary>
/// Displays one page of an HTML document rendered by PeachBrackets.
/// </summary>
/// <remarks>
/// <para>
/// Milestone-1 proof of concept: the page is rasterized on a background thread by PeachDrawing's CPU rasterizer
/// (<see cref="PeachHtmlRenderer"/>), copied into a <see cref="WriteableBitmap"/>, and drawn in <see cref="Render"/>.
/// Every relevant property change schedules a new render; a render still in flight is cancelled and its result
/// discarded.
/// </para>
/// <para>
/// Page size and margins are in points (1/72 inch), the unit PeachBrackets uses. The control's desired size is the
/// page size in device-independent pixels (1 pt = 96/72 DIP).
/// </para>
/// </remarks>
public class HtmlView : Control
{
    private const double DipsPerPoint = 96.0 / 72.0;

    /// <summary>The HTML to display. When non-null it wins over <see cref="Source"/>.</summary>
    public static readonly StyledProperty<string?> HtmlProperty =
        AvaloniaProperty.Register<HtmlView, string?>(nameof(Html));

    /// <summary>
    /// Where to load HTML from when <see cref="Html"/> is null: <c>file:</c>, <c>http(s):</c> or <c>avares:</c>.
    /// </summary>
    public static readonly StyledProperty<Uri?> SourceProperty =
        AvaloniaProperty.Register<HtmlView, Uri?>(nameof(Source));

    /// <summary>Sheet width in points (A4 = 595).</summary>
    public static readonly StyledProperty<double> PageWidthProperty =
        AvaloniaProperty.Register<HtmlView, double>(nameof(PageWidth), 595, validate: IsPositiveFinite);

    /// <summary>Sheet height in points (A4 = 842).</summary>
    public static readonly StyledProperty<double> PageHeightProperty =
        AvaloniaProperty.Register<HtmlView, double>(nameof(PageHeight), 842, validate: IsPositiveFinite);

    /// <summary>Page margins in points (PeachBrackets default is 20 on each side).</summary>
    public static readonly StyledProperty<Thickness> PageMarginProperty =
        AvaloniaProperty.Register<HtmlView, Thickness>(nameof(PageMargin), new Thickness(20));

    /// <summary>Zero-based page to show; clamped to the last page.</summary>
    public static readonly StyledProperty<int> PageIndexProperty =
        AvaloniaProperty.Register<HtmlView, int>(nameof(PageIndex), 0, validate: i => i >= 0);

    /// <summary>
    /// Extra raster resolution multiplier. 0 (default) means "match the screen": the bitmap is rendered at
    /// 96/72 pixels per point times the window's render scaling. Any positive value is used as-is (pixels per DIP).
    /// </summary>
    public static readonly StyledProperty<double> RenderScaleProperty =
        AvaloniaProperty.Register<HtmlView, double>(nameof(RenderScale), 0, validate: d => d >= 0 && !double.IsInfinity(d));

    /// <summary>Brush painted behind the page (PeachBrackets leaves unpainted areas transparent).</summary>
    public static readonly StyledProperty<IBrush?> PageBackgroundProperty =
        AvaloniaProperty.Register<HtmlView, IBrush?>(nameof(PageBackground), Brushes.White);

    /// <summary>Optional CSS applied before the document's own style sheets.</summary>
    public static readonly StyledProperty<string?> UserStyleSheetProperty =
        AvaloniaProperty.Register<HtmlView, string?>(nameof(UserStyleSheet));

    /// <summary>
    /// Address relative references (<c>&lt;img src&gt;</c>, <c>&lt;link href&gt;</c>, CSS <c>url()</c>) resolve against when the document has
    /// no <c>&lt;base href&gt;</c>. When null and the document came from <see cref="Source"/>, <see cref="Source"/> is used.
    /// </summary>
    public static readonly StyledProperty<Uri?> BaseUriProperty =
        AvaloniaProperty.Register<HtmlView, Uri?>(nameof(BaseUri));

    /// <summary>
    /// Opt-in: let the document fetch <c>http</c>/<c>https</c> images, style sheets and fonts. Off by default so displaying a document
    /// never makes the app call out. (Loading <see cref="Source"/> itself over http(s) is the app's explicit choice and is not affected.)
    /// </summary>
    public static readonly StyledProperty<bool> AllowNetworkProperty =
        AvaloniaProperty.Register<HtmlView, bool>(nameof(AllowNetwork));

    /// <summary>Let the document read <c>file:</c> references (default true). <c>data:</c> and <c>avares:</c> always load.</summary>
    public static readonly StyledProperty<bool> AllowLocalFilesProperty =
        AvaloniaProperty.Register<HtmlView, bool>(nameof(AllowLocalFiles), true);

    /// <summary>
    /// <see cref="MediaType.Print"/> (default) paginates onto the sheet; <see cref="MediaType.Screen"/> lays the document out as one
    /// continuous page of <see cref="PageWidth"/> and applies <c>@media screen</c>.
    /// </summary>
    public static readonly StyledProperty<MediaType> MediaTypeProperty =
        AvaloniaProperty.Register<HtmlView, MediaType>(nameof(MediaType), MediaType.Print);

    /// <summary>Number of pages the current document was laid out into (0 until the first render completes).</summary>
    public static readonly DirectProperty<HtmlView, int> PageCountProperty =
        AvaloniaProperty.RegisterDirect<HtmlView, int>(nameof(PageCount), o => o.PageCount);

    /// <summary>True while a render is in flight.</summary>
    public static readonly DirectProperty<HtmlView, bool> IsRenderingProperty =
        AvaloniaProperty.RegisterDirect<HtmlView, bool>(nameof(IsRendering), o => o.IsRendering);

    /// <summary>The last render failure, or null.</summary>
    public static readonly DirectProperty<HtmlView, Exception?> RenderErrorProperty =
        AvaloniaProperty.RegisterDirect<HtmlView, Exception?>(nameof(RenderError), o => o.RenderError);

    /// <summary>The document's &lt;title&gt;, or null.</summary>
    public static readonly DirectProperty<HtmlView, string?> DocumentTitleProperty =
        AvaloniaProperty.RegisterDirect<HtmlView, string?>(nameof(DocumentTitle), o => o.DocumentTitle);

    private static readonly HttpClient s_http = new();

    private CancellationTokenSource? _cts;
    private bool _renderQueued;
    private WriteableBitmap? _bitmap;
    private Size _pageSizeDips;
    private int _pageCount;
    private bool _isRendering;
    private Exception? _renderError;
    private string? _documentTitle;
    private PeachHtmlRenderer? _renderer;
    private TopLevel? _topLevel;

    static HtmlView()
    {
        AffectsMeasure<HtmlView>(PageWidthProperty, PageHeightProperty);
        AffectsRender<HtmlView>(PageBackgroundProperty);
    }

    /// <summary>Creates the control.</summary>
    public HtmlView()
    {
        _pageSizeDips = new Size(PageWidth * DipsPerPoint, PageHeight * DipsPerPoint);
    }

    /// <summary>Raised on the UI thread after each render completes or fails (not when it is superseded).</summary>
    public event EventHandler<HtmlRenderCompletedEventArgs>? RenderCompleted;

    /// <inheritdoc cref="HtmlProperty"/>
    public string? Html { get => GetValue(HtmlProperty); set => SetValue(HtmlProperty, value); }

    /// <inheritdoc cref="SourceProperty"/>
    public Uri? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }

    /// <inheritdoc cref="PageWidthProperty"/>
    public double PageWidth { get => GetValue(PageWidthProperty); set => SetValue(PageWidthProperty, value); }

    /// <inheritdoc cref="PageHeightProperty"/>
    public double PageHeight { get => GetValue(PageHeightProperty); set => SetValue(PageHeightProperty, value); }

    /// <inheritdoc cref="PageMarginProperty"/>
    public Thickness PageMargin { get => GetValue(PageMarginProperty); set => SetValue(PageMarginProperty, value); }

    /// <inheritdoc cref="PageIndexProperty"/>
    public int PageIndex { get => GetValue(PageIndexProperty); set => SetValue(PageIndexProperty, value); }

    /// <inheritdoc cref="RenderScaleProperty"/>
    public double RenderScale { get => GetValue(RenderScaleProperty); set => SetValue(RenderScaleProperty, value); }

    /// <inheritdoc cref="PageBackgroundProperty"/>
    public IBrush? PageBackground { get => GetValue(PageBackgroundProperty); set => SetValue(PageBackgroundProperty, value); }

    /// <inheritdoc cref="UserStyleSheetProperty"/>
    public string? UserStyleSheet { get => GetValue(UserStyleSheetProperty); set => SetValue(UserStyleSheetProperty, value); }

    /// <inheritdoc cref="BaseUriProperty"/>
    public Uri? BaseUri { get => GetValue(BaseUriProperty); set => SetValue(BaseUriProperty, value); }

    /// <inheritdoc cref="AllowNetworkProperty"/>
    public bool AllowNetwork { get => GetValue(AllowNetworkProperty); set => SetValue(AllowNetworkProperty, value); }

    /// <inheritdoc cref="AllowLocalFilesProperty"/>
    public bool AllowLocalFiles { get => GetValue(AllowLocalFilesProperty); set => SetValue(AllowLocalFilesProperty, value); }

    /// <inheritdoc cref="MediaTypeProperty"/>
    public MediaType MediaType { get => GetValue(MediaTypeProperty); set => SetValue(MediaTypeProperty, value); }

    /// <inheritdoc cref="PageCountProperty"/>
    public int PageCount { get => _pageCount; private set => SetAndRaise(PageCountProperty, ref _pageCount, value); }

    /// <inheritdoc cref="IsRenderingProperty"/>
    public bool IsRendering { get => _isRendering; private set => SetAndRaise(IsRenderingProperty, ref _isRendering, value); }

    /// <inheritdoc cref="RenderErrorProperty"/>
    public Exception? RenderError { get => _renderError; private set => SetAndRaise(RenderErrorProperty, ref _renderError, value); }

    /// <inheritdoc cref="DocumentTitleProperty"/>
    public string? DocumentTitle { get => _documentTitle; private set => SetAndRaise(DocumentTitleProperty, ref _documentTitle, value); }

    /// <summary>The renderer to use. Defaults to <see cref="PeachHtmlRenderer.Shared"/>.</summary>
    public PeachHtmlRenderer Renderer
    {
        get => _renderer ?? PeachHtmlRenderer.Shared;
        set { _renderer = value; InvalidateDocument(); }
    }

    /// <summary>The bitmap of the page currently shown, or null.</summary>
    public Bitmap? CurrentBitmap => _bitmap;

    /// <summary>Forces a new render with the current properties.</summary>
    public void InvalidateDocument() => QueueRender();

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        var p = change.Property;
        if (p == HtmlProperty || p == SourceProperty || p == PageWidthProperty || p == PageHeightProperty ||
            p == PageMarginProperty || p == PageIndexProperty || p == RenderScaleProperty || p == UserStyleSheetProperty ||
            p == BaseUriProperty || p == AllowNetworkProperty || p == AllowLocalFilesProperty || p == MediaTypeProperty)
        {
            QueueRender();
        }
    }

    /// <inheritdoc/>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel is not null)
            _topLevel.ScalingChanged += OnScalingChanged;
        QueueRender();
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_topLevel is not null)
            _topLevel.ScalingChanged -= OnScalingChanged;
        _topLevel = null;
        CancelInFlight();
    }

    private void OnScalingChanged(object? sender, EventArgs e)
    {
        if (RenderScale == 0) QueueRender();
    }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize) => _pageSizeDips;

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize) => _pageSizeDips;

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        var rect = new Rect(_pageSizeDips);
        if (PageBackground is { } bg)
            context.FillRectangle(bg, rect);
        if (_bitmap is { } bmp)
            context.DrawImage(bmp, new Rect(bmp.Size), rect);
    }

    private void QueueRender()
    {
        // Coalesce several property changes made in one dispatcher turn into one render.
        if (_renderQueued) return;
        _renderQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _renderQueued = false;
            _ = RenderAsync();
        }, DispatcherPriority.Background);
    }

    private void CancelInFlight()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private async Task RenderAsync()
    {
        CancelInFlight();
        if (VisualRoot is null) return;

        var html = Html;
        var source = Source;
        if (html is null && source is null)
        {
            ShowPage(null);
            return;
        }

        var cts = new CancellationTokenSource();
        _cts = cts;
        var ct = cts.Token;
        IsRendering = true;

        var page = new HtmlPageSettings(PageWidth, PageHeight,
            PageMargin.Left, PageMargin.Top, PageMargin.Right, PageMargin.Bottom);
        var scale = RenderScale > 0 ? RenderScale : (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0);
        var pixelsPerPoint = DipsPerPoint * scale;
        var userCss = UserStyleSheet;
        var index = PageIndex;
        var renderer = Renderer;
        var media = MediaType;
        var resources = new HtmlResourceOptions(
            BaseUri ?? (html is null ? source : null),
            AllowNetwork,
            AllowLocalFiles);

        RenderedHtmlPage? result = null;
        Exception? error = null;
        try
        {
            html ??= await LoadSourceAsync(source!, ct).ConfigureAwait(true);
            result = await renderer.RenderPageAsync(html, page, index, pixelsPerPoint, userCss, ct, resources, media).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return; // superseded by a newer render
        }
        catch (Exception ex)
        {
            error = ex;
        }

        if (!ReferenceEquals(_cts, cts) || ct.IsCancellationRequested)
            return; // superseded

        _cts = null;
        cts.Dispose();
        IsRendering = false;
        RenderError = error;
        if (result is not null)
            ShowPage(result);
        RenderCompleted?.Invoke(this, new HtmlRenderCompletedEventArgs(result, error));
    }

    private void ShowPage(RenderedHtmlPage? page)
    {
        var old = _bitmap;
        _bitmap = page is { PixelWidth: > 0, PixelHeight: > 0 } ? ToWriteableBitmap(page) : null;
        old?.Dispose();

        PageCount = page?.PageCount ?? 0;
        DocumentTitle = page?.Title;
        var size = page is null
            ? new Size(PageWidth * DipsPerPoint, PageHeight * DipsPerPoint)
            : new Size(page.WidthPoints * DipsPerPoint, page.HeightPoints * DipsPerPoint);
        if (size != _pageSizeDips)
        {
            _pageSizeDips = size;
            InvalidateMeasure();
        }
        InvalidateVisual();
    }

    /// <summary>Copies PeachDrawing's tightly packed premultiplied RGBA8 into a new <see cref="WriteableBitmap"/>.</summary>
    public static WriteableBitmap ToWriteableBitmap(RenderedHtmlPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        // DPI chosen so the bitmap's DIP size equals the page size: DIPs = px * 96 / dpi = pt * 96 / 72.
        var dpi = 72.0 * page.PixelsPerPoint;
        var bitmap = new WriteableBitmap(
            new PixelSize(page.PixelWidth, page.PixelHeight),
            new Vector(dpi, dpi),
            PixelFormat.Rgba8888,
            AlphaFormat.Premul);

        using var fb = bitmap.Lock();
        var srcStride = page.PixelWidth * 4;
        if (fb.RowBytes == srcStride)
        {
            System.Runtime.InteropServices.Marshal.Copy(page.PremultipliedRgba, 0, fb.Address, srcStride * page.PixelHeight);
        }
        else
        {
            for (var y = 0; y < page.PixelHeight; y++)
                System.Runtime.InteropServices.Marshal.Copy(page.PremultipliedRgba, y * srcStride, fb.Address + y * fb.RowBytes, srcStride);
        }
        return bitmap;
    }

    private static async Task<string> LoadSourceAsync(Uri source, CancellationToken ct)
    {
        if (!source.IsAbsoluteUri)
            throw new ArgumentException($"Source must be an absolute URI: '{source}'.", nameof(source));

        switch (source.Scheme)
        {
            case "file":
                return await File.ReadAllTextAsync(source.LocalPath, ct).ConfigureAwait(false);
            case "http":
            case "https":
                return await s_http.GetStringAsync(source, ct).ConfigureAwait(false);
            case "avares":
            {
                await using var stream = AssetLoader.Open(source);
                using var reader = new StreamReader(stream);
                return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
            }
            default:
                throw new NotSupportedException($"URI scheme '{source.Scheme}' is not supported (use file, http, https or avares).");
        }
    }

    private static bool IsPositiveFinite(double d) => d > 0 && !double.IsInfinity(d) && !double.IsNaN(d);
}
