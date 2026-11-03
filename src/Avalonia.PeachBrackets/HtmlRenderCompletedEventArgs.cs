namespace Avalonia.PeachBrackets;

/// <summary>Raised by <see cref="HtmlView"/> after a render finishes (successfully or not).</summary>
public sealed class HtmlRenderCompletedEventArgs : EventArgs
{
    internal HtmlRenderCompletedEventArgs(RenderedHtmlPage? page, Exception? error)
    {
        Page = page;
        Error = error;
    }

    /// <summary>The painted page, or null when the render failed.</summary>
    public RenderedHtmlPage? Page { get; }

    /// <summary>The failure, or null on success.</summary>
    public Exception? Error { get; }
}
