using PeachBrackets;
using Xunit;

namespace Avalonia.PeachBrackets.Tests;

/// <summary>Images, linked style sheets and media type, which need PeachBrackets 0.9.26's RasterRenderContext.ResourceLoader.</summary>
public sealed class ResourceLoadingTests : IDisposable
{
    // 8x8 solid PNGs: green (0,200,0) and magenta (255,0,255).
    private const string GreenPng = "iVBORw0KGgoAAAANSUhEUgAAAAgAAAAICAIAAABLbSncAAAAEElEQVR4nGNgOMGAHQ0tCQCJ5TIBafw8vQAAAABJRU5ErkJggg==";
    private const string MagentaPng = "iVBORw0KGgoAAAANSUhEUgAAAAgAAAAICAIAAABLbSncAAAAEUlEQVR4nGP4z/AfK2IYWhIA0ad/gXfwoGMAAAAASUVORK5CYII=";
    private static readonly HtmlPageSettings Sheet = new(300, 200, 10, 10, 10, 10);

    private readonly string _dir = Directory.CreateTempSubdirectory("peach-res-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private Uri DirUri => new(Path.TrimEndingDirectorySeparator(_dir) + Path.DirectorySeparatorChar);

    [Fact]
    public async Task Data_uri_image_is_painted()
    {
        var html = $"<html><body><img src=\"data:image/png;base64,{GreenPng}\" style=\"width:80px;height:80px\"></body></html>";
        var page = await PeachHtmlRenderer.Shared.RenderPageAsync(html, Sheet, resources: HtmlResourceOptions.DataOnly,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(Count(page, 0, 200, 0) > 1000, "expected the green data: image");
    }

    [Fact]
    public async Task Relative_file_image_and_stylesheet_resolve_against_BaseUri()
    {
        File.WriteAllBytes(Path.Combine(_dir, "m.png"), Convert.FromBase64String(MagentaPng));
        File.WriteAllText(Path.Combine(_dir, "s.css"), "div.box { width: 80px; height: 80px; background: rgb(0,0,255); }");
        const string html = "<html><head><link rel=\"stylesheet\" href=\"s.css\"></head><body>" +
                            "<img src=\"m.png\" style=\"width:80px;height:80px\"><div class=\"box\"></div></body></html>";

        var page = await PeachHtmlRenderer.Shared.RenderPageAsync(html, Sheet, resources: new HtmlResourceOptions(DirUri),
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(Count(page, 255, 0, 255) > 1000, "expected the magenta file image");
        Assert.True(Count(page, 0, 0, 255) > 1000, "expected the box styled by the linked sheet");

        var blocked = await PeachHtmlRenderer.Shared.RenderPageAsync(html, Sheet,
            resources: new HtmlResourceOptions(DirUri, AllowLocalFiles: false), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(0, Count(blocked, 255, 0, 255));
        Assert.Equal(0, Count(blocked, 0, 0, 255));
    }

    [Fact]
    public async Task Relative_link_without_any_base_does_not_fail_the_render()
    {
        // Unpatched, PeachBrackets 0.9.26 throws "operation is not supported for a relative URI" here.
        const string html = "<html><head><link rel=\"stylesheet\" href=\"missing.css\"></head><body><p>ok</p></body></html>";
        var page = await PeachHtmlRenderer.Shared.RenderPageAsync(html, Sheet, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(1, page.PageCount);
    }

    [Fact]
    public async Task Network_is_off_by_default()
    {
        // Unroutable on purpose: if the loader tried, the render would hang for the client timeout.
        const string html = "<html><body><img src=\"http://10.255.255.1/x.png\" style=\"width:80px;height:80px;background:rgb(0,0,255)\"></body></html>";
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var page = await PeachHtmlRenderer.Shared.RenderPageAsync(html, Sheet, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10));
        Assert.Equal(1, page.PageCount);
    }

    [Fact]
    public async Task Screen_media_lays_out_one_continuous_page()
    {
        var html = "<html><body>" + string.Concat(Enumerable.Repeat("<p>Lorem ipsum dolor sit amet.</p>", 100)) + "</body></html>";
        var page = await PeachHtmlRenderer.Shared.RenderPageAsync(html, Sheet, media: MediaType.Screen, pixelsPerPoint: 1,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(1, page.PageCount);
        Assert.True(page.HeightPoints > Sheet.HeightPoints, $"expected a tall surface, got {page.HeightPoints}");
    }

    private static int Count(RenderedHtmlPage p, int r, int g, int b)
    {
        var n = 0;
        var px = p.PremultipliedRgba;
        for (var i = 0; i < px.Length; i += 4)
            if (px[i + 3] == 255 && Math.Abs(px[i] - r) < 8 && Math.Abs(px[i + 1] - g) < 8 && Math.Abs(px[i + 2] - b) < 8) n++;
        return n;
    }
}
