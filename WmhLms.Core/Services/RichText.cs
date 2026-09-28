using System.Net;
using System.Text.RegularExpressions;
using Ganss.Xss;

namespace WmhLms.Core.Services;

/// <summary>
/// Text-item content is stored as HTML so pasted Word / Google Docs / PDF text
/// keeps its headings, lists, tables and links. Agents' browsers render it as
/// markup, so everything that reaches the database passes through here first:
/// an allowlist of structural tags, no styles, no scripts, no event handlers,
/// and links limited to http(s) and mailto. A compromised manager account must
/// not be able to plant script that runs in every agent's session.
///
/// Content with no tags at all (older items, API callers sending plain text) is
/// converted to paragraphs, so after any save the column holds HTML.
/// </summary>
public static partial class RichText
{
    public const int MaxLength = 200_000;

    private static readonly HtmlSanitizer Sanitizer = new(new HtmlSanitizerOptions
    {
        AllowedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "p", "br", "h1", "h2", "h3", "h4", "strong", "b", "em", "i", "u", "s",
            "ul", "ol", "li", "blockquote", "hr", "a", "code", "pre",
            "table", "thead", "tbody", "tr", "th", "td"
        },
        AllowedAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "href", "colspan", "rowspan"
        },
        AllowedSchemes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "http", "https", "mailto"
        },
        UriAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "href" },
        AllowedCssProperties = new HashSet<string>()
    })
    {
        // Pasted documents wrap text in <div>/<span>/<font>. Dropping those
        // wrappers must not drop the words inside them.
        KeepChildNodes = true
    };

    [GeneratedRegex(@"<\s*/?\s*[a-zA-Z!]")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"\r\n?")]
    private static partial Regex LineEndings();

    [GeneratedRegex(@"\n\s*\n")]
    private static partial Regex BlankLines();

    public static bool LooksLikeHtml(string? value) =>
        !string.IsNullOrEmpty(value) && TagPattern().IsMatch(value);

    /// <summary>Sanitises HTML, or converts plain text to paragraphs. Empty stays empty.</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        if (value.Length > MaxLength)
            throw ApiException.BadRequest(
                $"Text content must be {MaxLength:N0} characters or fewer.");
        var html = LooksLikeHtml(value) ? Sanitizer.Sanitize(value) : FromPlainText(value);
        return string.IsNullOrWhiteSpace(html) ? "" : html.Trim();
    }

    /// <summary>
    /// Blank lines separate paragraphs; single line breaks are kept inside them.
    /// Everything is HTML-encoded, so plain text can never become markup.
    /// </summary>
    public static string FromPlainText(string text)
    {
        var normalised = LineEndings().Replace(text.Trim(), "\n");
        var paragraphs = BlankLines().Split(normalised)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .Select(p => "<p>" + WebUtility.HtmlEncode(p).Replace("\n", "<br>") + "</p>");
        return string.Concat(paragraphs);
    }
}
