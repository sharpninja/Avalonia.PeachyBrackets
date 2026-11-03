namespace Avalonia.PeachBrackets;

/// <summary>
/// The sheet a document is paginated onto. Every length is in points (1/72 inch), matching PeachBrackets.
/// A <c>@page</c> rule in the document overrides these values.
/// </summary>
/// <param name="WidthPoints">Sheet width in points. A4 is 595.</param>
/// <param name="HeightPoints">Sheet height in points. A4 is 842.</param>
/// <param name="MarginLeft">Left margin in points.</param>
/// <param name="MarginTop">Top margin in points.</param>
/// <param name="MarginRight">Right margin in points.</param>
/// <param name="MarginBottom">Bottom margin in points.</param>
public readonly record struct HtmlPageSettings(
    double WidthPoints,
    double HeightPoints,
    double MarginLeft,
    double MarginTop,
    double MarginRight,
    double MarginBottom)
{
    /// <summary>ISO A4 portrait (595 x 842 pt) with PeachBrackets' default 20 pt margins.</summary>
    public static HtmlPageSettings A4 { get; } = new(595, 842, 20, 20, 20, 20);

    /// <summary>US Letter portrait (612 x 792 pt) with 20 pt margins.</summary>
    public static HtmlPageSettings Letter { get; } = new(612, 792, 20, 20, 20, 20);

    /// <summary>Returns a copy with all four margins set to <paramref name="points"/>.</summary>
    public HtmlPageSettings WithUniformMargin(double points) => this with
    {
        MarginLeft = points, MarginTop = points, MarginRight = points, MarginBottom = points,
    };
}
