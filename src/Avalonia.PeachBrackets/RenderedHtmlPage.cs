namespace Avalonia.PeachBrackets;

/// <summary>
/// One page painted by <see cref="PeachHtmlRenderer"/>: tightly packed, premultiplied RGBA8 pixels
/// (<c>PixelWidth * PixelHeight * 4</c> bytes, row-major, no padding) plus what the layout reported.
/// </summary>
public sealed class RenderedHtmlPage
{
    internal RenderedHtmlPage(
        int pageIndex, int pageCount, double widthPoints, double heightPoints,
        double pixelsPerPoint, int pixelWidth, int pixelHeight, byte[] premultipliedRgba,
        string? title, TimeSpan elapsed)
    {
        PageIndex = pageIndex;
        PageCount = pageCount;
        WidthPoints = widthPoints;
        HeightPoints = heightPoints;
        PixelsPerPoint = pixelsPerPoint;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        PremultipliedRgba = premultipliedRgba;
        Title = title;
        Elapsed = elapsed;
    }

    /// <summary>Zero-based index of the painted page.</summary>
    public int PageIndex { get; }

    /// <summary>How many pages the document was laid out into.</summary>
    public int PageCount { get; }

    /// <summary>Width of this page's sheet in points (may differ from the request when <c>@page</c> sets a size).</summary>
    public double WidthPoints { get; }

    /// <summary>Height of this page's sheet in points.</summary>
    public double HeightPoints { get; }

    /// <summary>Device pixels per point the page was rasterized at.</summary>
    public double PixelsPerPoint { get; }

    /// <summary>Bitmap width in pixels.</summary>
    public int PixelWidth { get; }

    /// <summary>Bitmap height in pixels.</summary>
    public int PixelHeight { get; }

    /// <summary>Premultiplied RGBA8 pixels. Areas the document leaves unpainted are transparent.</summary>
    public byte[] PremultipliedRgba { get; }

    /// <summary>The document's <c>&lt;title&gt;</c>, or null.</summary>
    public string? Title { get; }

    /// <summary>Wall-clock time spent loading, laying out and painting.</summary>
    public TimeSpan Elapsed { get; }
}
