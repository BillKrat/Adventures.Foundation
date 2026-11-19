using Xunit;

namespace Adventures.Blog.Tests;

/// <summary>The one place HTML written in the visual editor is made safe: kept for formatting, stripped of anything that can run or phone home.</summary>
public sealed class HtmlContentTests
{
    [Theory]
    [InlineData("<p>Hello <strong>bold</strong> <em>it</em> <u>under</u></p>", "<strong>bold</strong>")]
    [InlineData("<h2>Title</h2><ul><li>one</li></ul><ol><li>two</li></ol>", "<li>one</li>")]
    [InlineData("<blockquote>quote</blockquote><pre><code>x = 1</code></pre>", "<pre>")]
    [InlineData("<table><tbody><tr><td>a</td></tr></tbody></table>", "<td>a</td>")]
    [InlineData("<a href=\"https://example.com/x\">link</a>", "href=\"https://example.com/x\"")]
    [InlineData("<img src=\"/api/media/abc\" alt=\"a chart\">", "src=\"/api/media/abc\"")]
    public void Formatting_a_person_applies_is_kept(string html, string expected)
    {
        Assert.Contains(expected, HtmlContent.Sanitize(html));
    }

    [Theory]
    [InlineData("<p style=\"text-align: center;\">centred</p>", "text-align")]
    [InlineData("<span style=\"color: #ff0000;\">red</span>", "color")]
    [InlineData("<span style=\"background-color: yellow;\">hi</span>", "background-color")]
    public void Alignment_and_colour_survive_because_a_word_like_editor_needs_them(string html, string expected)
    {
        Assert.Contains(expected, HtmlContent.Sanitize(html));
    }

    [Theory]
    [InlineData("<p>a</p><script>alert(1)</script>", "script")]
    [InlineData("<img src=\"x\" onerror=\"alert(1)\">", "onerror")]
    [InlineData("<p onclick=\"alert(1)\">x</p>", "onclick")]
    [InlineData("<a href=\"javascript:alert(1)\">x</a>", "javascript")]
    [InlineData("<iframe src=\"https://evil.example\"></iframe>", "iframe")]
    [InlineData("<object data=\"x\"></object><embed src=\"x\">", "embed")]
    [InlineData("<form action=\"https://evil.example\"><input name=\"a\"></form>", "<form")]
    [InlineData("<style>body{display:none}</style>", "<style")]
    [InlineData("<link rel=\"stylesheet\" href=\"https://evil.example/x.css\">", "<link")]
    [InlineData("<meta http-equiv=\"refresh\" content=\"0;url=https://evil.example\">", "<meta")]
    [InlineData("<svg onload=\"alert(1)\"></svg>", "onload")]
    [InlineData("<p style=\"background: url(https://evil.example/t.gif)\">x</p>", "evil.example")]
    [InlineData("<p style=\"width: expression(alert(1))\">x</p>", "expression")]
    [InlineData("<a href=\"data:text/html;base64,PHNjcmlwdD4=\">x</a>", "data:")]
    public void Anything_that_can_run_or_load_from_elsewhere_is_removed(string html, string forbidden)
    {
        Assert.DoesNotContain(forbidden, HtmlContent.Sanitize(html), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Links_that_open_in_a_new_window_cannot_reach_back_to_the_site()
    {
        var cleaned = HtmlContent.Sanitize("<a href=\"https://example.com\" target=\"_blank\">x</a>");

        Assert.Contains("noopener", cleaned);
    }

    [Fact]
    public void Nothing_in_gives_nothing_out()
    {
        Assert.Equal(string.Empty, HtmlContent.Sanitize(null));
        Assert.Equal(string.Empty, HtmlContent.Sanitize("   "));
    }

    [Fact]
    public void Plain_text_is_left_alone()
    {
        Assert.Equal("Just words & more", HtmlContent.Sanitize("Just words &amp; more").Replace("&amp;", "&"));
    }
}
