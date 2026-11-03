using Avalonia.Headless;
using Avalonia.Threading;

namespace Avalonia.PeachBrackets.Demo;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var i = Array.IndexOf(args, "--screenshot");
        if (i >= 0)
            return HeadlessScreenshot(i + 1 < args.Length ? args[i + 1] : "docs");

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    // Used by the visual designer and by the desktop entry point.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();

    /// <summary>
    /// Renders the demo window with Avalonia.Headless + Skia (no display needed) and saves
    /// <c>demo-screenshot.png</c> (the whole window) and <c>demo-page.png</c> (the raw PeachBrackets page bitmap).
    /// </summary>
    private static int HeadlessScreenshot(string outDir)
    {
        Directory.CreateDirectory(outDir);
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithInterFont()
            .SetupWithoutStarting();

        var window = new MainWindow();
        window.Show();

        var done = false;
        window.HtmlView.RenderCompleted += (_, _) => done = true;
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (!done && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
        if (!done)
        {
            Console.Error.WriteLine("Timed out waiting for the page to render.");
            return 1;
        }
        if (window.HtmlView.RenderError is { } err)
        {
            Console.Error.WriteLine($"Render failed: {err}");
            return 1;
        }

        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var frame = window.CaptureRenderedFrame();
        if (frame is null)
        {
            Console.Error.WriteLine("No frame captured.");
            return 1;
        }
        var shot = Path.Combine(outDir, "demo-screenshot.png");
        frame.Save(shot, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());

        var page = Path.Combine(outDir, "demo-page.png");
        window.HtmlView.CurrentBitmap!.Save(page, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        Console.WriteLine($"Saved {shot} ({frame.PixelSize}) and {page} ({window.HtmlView.CurrentBitmap.PixelSize}); pages={window.HtmlView.PageCount}");
        return 0;
    }
}
