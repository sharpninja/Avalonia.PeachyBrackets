using System.Net;
using System.Text.RegularExpressions;

namespace Avalonia.PeachBrackets;

/// <summary>
/// Works around PeachBrackets 0.9.26 resolving relative references without a document base:
/// <list type="bullet">
/// <item><c>RasterRenderContext.BaseUri</c> is always null (the class is sealed), and <c>&lt;link rel="stylesheet"&gt;</c> is loaded
/// before the engine has seen <c>&lt;base href&gt;</c>. A relative <c>href</c> then reaches <c>StylesheetLoadHandler</c> as a relative
/// URI and the whole load fails with "This operation is not supported for a relative URI".</item>
/// <item>Images and CSS <c>url()</c> are resolved during layout and do honour <c>&lt;base href&gt;</c>.</item>
/// </list>
/// So, before parsing: every relative <c>&lt;link href&gt;</c> is made absolute against the effective base (the document's own
/// <c>&lt;base href&gt;</c>, else <see cref="HtmlResourceOptions.BaseUri"/>), or neutralised when there is none, and a
/// <c>&lt;base href&gt;</c> is injected when the document has none and a base is known.
/// </summary>
internal static partial class HtmlBaseUriRewriter
{
    private const string EmptyStyleSheet = "data:text/css,";

    [GeneratedRegex("""<base\b[^>]*?\bhref\s*=\s*(?:"(?<v>[^"]*)"|'(?<v>[^']*)'|(?<v>[^\s>]+))""", RegexOptions.IgnoreCase)]
    private static partial Regex BaseHref();

    [GeneratedRegex("""<link\b[^>]*>""", RegexOptions.IgnoreCase)]
    private static partial Regex LinkTag();

    [GeneratedRegex("""(?<pre>\bhref\s*=\s*)(?:"(?<v>[^"]*)"|'(?<v>[^']*)'|(?<v>[^\s>]+))""", RegexOptions.IgnoreCase)]
    private static partial Regex HrefAttr();

    [GeneratedRegex("""<head\b[^>]*>""", RegexOptions.IgnoreCase)]
    private static partial Regex HeadTag();

    public static string Rewrite(string html, Uri? baseUri)
    {
        if (html.IndexOf("<link", StringComparison.OrdinalIgnoreCase) < 0 && baseUri is null)
            return html;

        var effective = baseUri is { IsAbsoluteUri: true } ? baseUri : null;
        var hasBase = false;
        if (BaseHref().Match(html) is { Success: true } m)
        {
            hasBase = true;
            var own = WebUtility.HtmlDecode(m.Groups["v"].Value.Trim());
            if (effective is not null && Uri.TryCreate(effective, own, out var a)) effective = a;
            else if (Uri.TryCreate(own, UriKind.Absolute, out var b)) effective = b;
        }

        html = LinkTag().Replace(html, link => HrefAttr().Replace(link.Value, href =>
        {
            var value = WebUtility.HtmlDecode(href.Groups["v"].Value.Trim());
            if (Uri.TryCreate(value, UriKind.Absolute, out _))
                return href.Value;
            var resolved = effective is not null && Uri.TryCreate(effective, value, out var abs) ? abs.AbsoluteUri : EmptyStyleSheet;
            return $"{href.Groups["pre"].Value}\"{WebUtility.HtmlEncode(resolved)}\"";
        }, 1));

        if (!hasBase && effective is not null)
        {
            var tag = $"<base href=\"{WebUtility.HtmlEncode(effective.AbsoluteUri)}\">";
            var head = HeadTag().Match(html);
            html = head.Success ? html.Insert(head.Index + head.Length, tag) : tag + html;
        }

        return html;
    }
}
