namespace Avalonia.PeachBrackets;

/// <summary>
/// How a document's own references (<c>&lt;img&gt;</c>, <c>&lt;link rel="stylesheet"&gt;</c>, <c>@import</c>, CSS <c>url()</c>,
/// <c>@font-face</c>) are loaded. Requests go through PeachBrackets' <c>HostResourceLoader</c> (<c>data:</c>, <c>file:</c>,
/// <c>http(s):</c>), plus Avalonia <c>avares:</c> assets.
/// </summary>
/// <param name="BaseUri">
/// Absolute address that relative references resolve against when the document has no <c>&lt;base href&gt;</c>
/// (for example the folder or page the HTML came from). Null leaves relative references unresolved.
/// </param>
/// <param name="AllowNetwork">Fetch <c>http</c>/<c>https</c> references. Off by default so a document cannot make the app call out.</param>
/// <param name="AllowLocalFiles">Read <c>file:</c> references.</param>
/// <param name="AllowAvaloniaResources">Read <c>avares:</c> references through Avalonia's <c>AssetLoader</c>.</param>
public sealed record HtmlResourceOptions(
    Uri? BaseUri = null,
    bool AllowNetwork = false,
    bool AllowLocalFiles = true,
    bool AllowAvaloniaResources = true)
{
    /// <summary>Loads <c>data:</c>, <c>file:</c> and <c>avares:</c> references; no network.</summary>
    public static HtmlResourceOptions Default { get; } = new();

    /// <summary>Loads nothing but <c>data:</c> URIs.</summary>
    public static HtmlResourceOptions DataOnly { get; } = new(AllowLocalFiles: false, AllowAvaloniaResources: false);
}
