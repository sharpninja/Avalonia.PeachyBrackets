using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Avalonia.PeachBrackets.Demo;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        RegisterDemoFonts();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// PeachDrawing's RasterRenderContext registers the machine's installed fonts, but MathML needs a font with the
    /// Mathematical Alphanumeric Symbols block (U+1D400..) and an OpenType MATH table, which most systems lack.
    /// The demo ships Noto Sans Math (SIL OFL 1.1, see Assets/Fonts/OFL.txt) and registers it; sample.html selects it
    /// with <c>math { font-family: 'Noto Sans Math' }</c>. Registration is queued ahead of any render on the shared renderer.
    /// </summary>
    private static void RegisterDemoFonts()
    {
        var uri = new Uri("avares://Avalonia.PeachBrackets.Demo/Assets/Fonts/NotoSansMath-Regular.ttf");
        // Copy the asset to memory, then register off the UI thread so the async font load cannot deadlock on it.
        var bytes = new MemoryStream();
        using (var asset = Avalonia.Platform.AssetLoader.Open(uri))
            asset.CopyTo(bytes);
        bytes.Position = 0;
        Task.Run(() => PeachHtmlRenderer.Shared.RegisterFontAsync(bytes, "Noto Sans Math")).GetAwaiter().GetResult();
    }
}
