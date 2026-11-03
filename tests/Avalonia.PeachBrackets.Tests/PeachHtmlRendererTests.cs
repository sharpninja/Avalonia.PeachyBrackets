using Xunit;

namespace Avalonia.PeachBrackets.Tests;

public sealed class PeachHtmlRendererTests
{
    private const string Html = """
        <html><head><title>T</title></head><body style="font-family: sans-serif">
        <h1 style="color: rgb(200,0,0)">Hello</h1>
        <svg width="60" height="60"><rect width="60" height="60" fill="rgb(0,0,255)"/></svg>
        </body></html>
        """;

    [Fact]
    public async Task Renders_first_page_with_visible_pixels_at_requested_scale()
    {
        var page = await PeachHtmlRenderer.Shared.RenderPageAsync(Html, new HtmlPageSettings(300, 200, 10, 10, 10, 10), pixelsPerPoint: 2,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, page.PageCount);
        Assert.Equal(600, page.PixelWidth);
        Assert.Equal(400, page.PixelHeight);
        Assert.Equal(600 * 400 * 4, page.PremultipliedRgba.Length);
        Assert.Equal("T", page.Title);
        Assert.True(CountOpaque(page) > 1000, "expected painted content");
        Assert.True(HasColor(page, r => r > 150, g => g < 60, b => b < 60), "expected the red heading");
        Assert.True(HasColor(page, r => r < 30, g => g < 30, b => b > 200), "expected the blue inline SVG rect");
    }

    [Fact]
    public async Task Long_document_paginates_and_page_index_is_clamped()
    {
        var html = "<html><body>" + string.Concat(Enumerable.Repeat("<p>Lorem ipsum dolor sit amet, consectetur adipiscing elit.</p>", 200)) + "</body></html>";
        var page = await PeachHtmlRenderer.Shared.RenderPageAsync(html, HtmlPageSettings.A4, pageIndex: 999,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(page.PageCount > 1);
        Assert.Equal(page.PageCount - 1, page.PageIndex);
    }

    [Fact]
    public async Task Cancelled_token_throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PeachHtmlRenderer.Shared.RenderPageAsync(Html, HtmlPageSettings.A4, cancellationToken: cts.Token));
    }

    private static int CountOpaque(RenderedHtmlPage p)
    {
        var n = 0;
        for (var i = 3; i < p.PremultipliedRgba.Length; i += 4) if (p.PremultipliedRgba[i] > 200) n++;
        return n;
    }

    private static bool HasColor(RenderedHtmlPage p, Func<byte, bool> r, Func<byte, bool> g, Func<byte, bool> b)
    {
        var px = p.PremultipliedRgba;
        for (var i = 0; i < px.Length; i += 4)
            if (px[i + 3] == 255 && r(px[i]) && g(px[i + 1]) && b(px[i + 2])) return true;
        return false;
    }
}
