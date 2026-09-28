using WmhLms.Core.Services;

namespace WmhLms.Tests;

/// <summary>
/// Text items are rendered as markup in every agent's browser, so what the
/// sanitiser lets through is a security boundary. The first group pins what
/// must never survive; the second pins what a pasted document must keep.
/// </summary>
public class RichTextTests
{
    // ── Never survives ──

    [Fact]
    public void Script_tags_are_removed() =>
        Assert.DoesNotContain("<script", RichText.Normalize("<p>Hi</p><script>alert(1)</script>"));

    [Fact]
    public void Event_handlers_are_removed() =>
        Assert.DoesNotContain("onerror", RichText.Normalize("<p onclick=\"x()\">Hi</p><img src=x onerror=alert(1)>"));

    [Fact]
    public void Javascript_links_lose_their_href()
    {
        var html = RichText.Normalize("<a href=\"javascript:alert(1)\">click</a>");
        Assert.DoesNotContain("javascript:", html);
        Assert.Contains("click", html);
    }

    [Fact]
    public void Inline_styles_and_classes_are_removed()
    {
        var html = RichText.Normalize("<p style=\"color:red\" class=\"MsoNormal\">Hi</p>");
        Assert.Equal("<p>Hi</p>", html);
    }

    [Fact]
    public void Iframes_and_images_are_removed()
    {
        var html = RichText.Normalize("<p>A</p><iframe src=\"https://evil\"></iframe><img src=\"https://x/y.png\">");
        Assert.DoesNotContain("iframe", html);
        Assert.DoesNotContain("img", html);
    }

    // ── Pasted documents keep their structure ──

    [Fact]
    public void Headings_lists_and_emphasis_are_kept()
    {
        const string input = "<h2>Title</h2><ul><li><strong>Bold</strong> and <em>italic</em></li></ul><ol><li>One</li></ol>";
        Assert.Equal(input, RichText.Normalize(input));
    }

    [Fact]
    public void Tables_are_kept_with_spans()
    {
        const string input = "<table><tbody><tr><th colspan=\"2\">Head</th></tr><tr><td>A</td><td>B</td></tr></tbody></table>";
        Assert.Equal(input, RichText.Normalize(input));
    }

    [Fact]
    public void Web_links_are_kept()
    {
        var html = RichText.Normalize("<a href=\"https://example.com/sop\">SOP</a>");
        Assert.Contains("href=\"https://example.com/sop\"", html);
    }

    [Fact]
    public void Text_inside_dropped_wrappers_is_kept() =>
        Assert.Equal("<p>Kept</p>", RichText.Normalize("<div><p><span style=\"font-family:Calibri\">Kept</span></p></div>"));

    // ── Plain text ──

    [Fact]
    public void Plain_text_becomes_paragraphs_with_line_breaks() =>
        Assert.Equal("<p>Line one<br>Line two</p><p>Second paragraph</p>",
            RichText.Normalize("Line one\r\nLine two\n\nSecond paragraph"));

    [Fact]
    public void Plain_text_is_encoded_rather_than_parsed() =>
        Assert.Equal("<p>a &amp; b &gt; c</p>", RichText.Normalize("a & b > c"));

    [Fact]
    public void Blank_content_stays_empty() =>
        Assert.Equal("", RichText.Normalize("   "));

    [Fact]
    public void Oversized_content_is_refused()
    {
        var ex = Assert.Throws<ApiException>(() => RichText.Normalize(new string('a', RichText.MaxLength + 1)));
        Assert.Equal(400, ex.StatusCode);
    }
}
