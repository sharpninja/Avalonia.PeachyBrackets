using Avalonia.Controls;
using Avalonia.Platform;

namespace Avalonia.PeachBrackets.Demo;

public partial class MainWindow : Window
{
    private static readonly (double W, double H)[] s_sizes = [(595, 842), (612, 792), (420, 595), (842, 595)];
    private bool _loadingEditor;

    public MainWindow()
    {
        InitializeComponent();

        // The view starts from its avares:// Source; mirror that HTML into the editor.
        using (var stream = AssetLoader.Open(new Uri("avares://Avalonia.PeachBrackets.Demo/Assets/sample.html")))
        using (var reader = new StreamReader(stream))
        {
            _loadingEditor = true;
            HtmlEditor.Text = reader.ReadToEnd();
            _loadingEditor = false;
        }

        ApplyPageOptions();
        PageSizeBox.SelectionChanged += (_, _) => ApplyPageOptions();
        MarginBox.ValueChanged += (_, _) => ApplyPageOptions();
        PageBox.ValueChanged += (_, _) => View.PageIndex = Math.Max(0, (int)(PageBox.Value ?? 1) - 1);
        HtmlEditor.TextChanged += (_, _) =>
        {
            if (!_loadingEditor) View.Html = HtmlEditor.Text ?? string.Empty; // Html wins over Source
        };

        View.RenderCompleted += (_, e) =>
        {
            PageCountText.Text = $"of {View.PageCount}";
            StatusText.Text = e.Error is { } err
                ? $"Render failed: {err.GetType().Name}: {err.Message}"
                : $"{e.Page!.PixelWidth}x{e.Page.PixelHeight}px, {e.Page.Elapsed.TotalMilliseconds:0} ms" +
                  (e.Page.Title is { } t ? $" - \"{t}\"" : "");
            if (e.Page?.Title is { } title) Title = $"{title} - Avalonia.PeachBrackets demo";
        };
    }

    /// <summary>The page view, for the headless screenshot runner.</summary>
    public HtmlView HtmlView => View;

    private void ApplyPageOptions()
    {
        var (w, h) = s_sizes[Math.Clamp(PageSizeBox.SelectedIndex, 0, s_sizes.Length - 1)];
        View.PageWidth = w;
        View.PageHeight = h;
        View.PageMargin = new Thickness((double)(MarginBox.Value ?? 36));
    }
}
