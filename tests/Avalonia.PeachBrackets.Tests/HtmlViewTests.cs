using Xunit;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace Avalonia.PeachBrackets.Tests;

public sealed class HtmlViewTests
{
    [AvaloniaFact]
    public async Task Renders_html_into_a_bitmap_and_reports_page_count()
    {
        var view = new HtmlView { PageWidth = 200, PageHeight = 100, Html = "<p>Hi</p>" };
        var window = new Window { Content = view, Width = 400, Height = 300 };
        window.Show();

        await WaitForRenderAsync(view);

        Assert.Null(view.RenderError);
        Assert.Equal(1, view.PageCount);
        Assert.NotNull(view.CurrentBitmap);
        // DesiredSize is layout-rounded; the page is 200 x 100 pt = 266.67 x 133.33 DIP.
        Assert.InRange(view.DesiredSize.Width, 200 * 96.0 / 72 - 1, 200 * 96.0 / 72 + 1);
        Assert.InRange(view.DesiredSize.Height, 100 * 96.0 / 72 - 1, 100 * 96.0 / 72 + 1);
    }

    [AvaloniaFact]
    public async Task Property_change_rerenders_and_supersedes_previous_render()
    {
        var view = new HtmlView { PageWidth = 200, PageHeight = 100, Html = "<p>one</p>" };
        var window = new Window { Content = view };
        window.Show();
        view.Html = "<p>two</p>";
        view.PageWidth = 300;

        var completions = 0;
        view.RenderCompleted += (_, _) => completions++;
        await WaitForRenderAsync(view);

        Assert.Equal(1, completions); // coalesced: one render for the final state
        Assert.Equal(300 * 96.0 / 72, view.CurrentBitmap!.Size.Width, 3);
    }

    [AvaloniaFact]
    public async Task Bad_source_reports_error()
    {
        var view = new HtmlView { Source = new Uri("file:///definitely/not/here.html") };
        var window = new Window { Content = view };
        window.Show();
        await WaitForRenderAsync(view);
        Assert.NotNull(view.RenderError);
    }

    private static async Task WaitForRenderAsync(HtmlView view)
    {
        var tcs = new TaskCompletionSource();
        view.RenderCompleted += (_, _) => tcs.TrySetResult();
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!tcs.Task.IsCompleted && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Assert.True(tcs.Task.IsCompleted, "render did not complete");
    }
}
