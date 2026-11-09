using SaronidaSailing.Infrastructure.Content;

namespace SaronidaSailing.Tests;

public sealed class RichTextSanitizerTests
{
    private readonly RichTextSanitizer _sanitizer = new();

    [Theory]
    [InlineData("<p>One</p><p>Two<br>three</p>")]
    [InlineData("<p><strong>bold</strong> and <em>italic</em></p>")]
    [InlineData("<ul><li>a</li><li>b</li></ul>")]
    [InlineData("<ol><li>a</li></ol>")]
    [InlineData("<h3>Title</h3><p>Body</p>")]
    [InlineData("<p>Καλημέρα κόσμε</p>")]
    public void AllowedMarkup_IsKept(string html) => Assert.Equal(html, _sanitizer.Sanitize(html));

    [Theory]
    [InlineData("<p>a</p><script>alert(1)</script>", "<p>a</p>")]
    [InlineData("<p>a</p><style>p{color:red}</style>", "<p>a</p>")]
    [InlineData("<p>a</p><iframe src=\"https://example.com\">fallback</iframe>", "<p>a</p>")]
    [InlineData("<div><script>alert(1)</script><p>a</p></div>", "<p>a</p>")]
    public void ScriptStyleAndIframe_AreRemovedWithTheirContent(string html, string expected) =>
        Assert.Equal(expected, _sanitizer.Sanitize(html));

    [Theory]
    [InlineData("<p><span>one</span> <b>two</b> <i>three</i> <u>four</u></p>", "<p>one two three four</p>")]
    [InlineData("<div><p>a</p></div>", "<p>a</p>")]
    [InlineData("<div>plain</div>", "plain")]
    [InlineData("<h1>Big</h1><h2>Medium</h2>", "BigMedium")]
    public void OtherElements_AreUnwrappedKeepingText(string html, string expected) =>
        Assert.Equal(expected, _sanitizer.Sanitize(html));

    [Theory]
    [InlineData("<p style=\"color:red\">a</p>")]
    [InlineData("<p class=\"x\">a</p>")]
    [InlineData("<p id=\"x\">a</p>")]
    [InlineData("<p onclick=\"alert(1)\">a</p>")]
    [InlineData("<p data-x=\"1\">a</p>")]
    public void DisallowedAttributes_AreRemoved(string html) => Assert.Equal("<p>a</p>", _sanitizer.Sanitize(html));

    [Fact]
    public void LinkAttributes_TargetRelAndClassAreRemoved()
    {
        var result = _sanitizer.Sanitize("<a href=\"https://example.com\" target=\"_blank\" rel=\"opener\" class=\"x\">go</a>");

        Assert.Equal("<a href=\"https://example.com\">go</a>", result);
    }

    [Theory]
    [InlineData("<p href=\"https://example.com\">a</p>", "<p>a</p>")]
    [InlineData("<h3 href=\"https://example.com\">t</h3>", "<h3>t</h3>")]
    [InlineData("<ul><li href=\"https://example.com\">x</li></ul>", "<ul><li>x</li></ul>")]
    [InlineData("<p><strong href=\"https://example.com\">b</strong></p>", "<p><strong>b</strong></p>")]
    public void Href_IsOnlyKeptOnAnchors(string html, string expected) =>
        Assert.Equal(expected, _sanitizer.Sanitize(html));

    [Fact]
    public void Href_IsKeptOnAnchors() =>
        Assert.Equal("<a href=\"https://example.com\">x</a>", _sanitizer.Sanitize("<a href=\"https://example.com\">x</a>"));

    [Theory]
    [InlineData("https://example.com/path")]
    [InlineData("mailto:info@example.com")]
    [InlineData("tel:+302291000000")]
    public void AllowedLinkSchemes_AreKept(string url) =>
        Assert.Equal($"<p><a href=\"{url}\">go</a></p>", _sanitizer.Sanitize($"<p><a href=\"{url}\">go</a></p>"));

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JaVaScRiPt:alert(1)")]
    [InlineData("http://example.com")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("/relative/path")]
    [InlineData("relative")]
    [InlineData("//example.com")]
    public void DisallowedLinkTargets_LoseTheirHrefButKeepTheText(string url)
    {
        var result = _sanitizer.Sanitize($"<p><a href=\"{url}\">go</a></p>");

        Assert.DoesNotContain("href", result);
        Assert.Contains("go", result);
        Assert.DoesNotContain("javascript", result, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<p><br></p>")]
    [InlineData("<p><br/></p>")]
    [InlineData("<p> </p>")]
    [InlineData("<p><br></p><p><br></p>")]
    [InlineData("<script>alert(1)</script>")]
    public void EmptyContent_BecomesEmptyString(string html) => Assert.Equal("", _sanitizer.Sanitize(html));

    [Fact]
    public void Result_IsTrimmed() => Assert.Equal("<p>a</p>", _sanitizer.Sanitize("  \n<p>a</p>\n "));

    [Fact]
    public void ParagraphWithText_IsNotTreatedAsEmpty() =>
        Assert.Equal("<p><br></p><p>a</p>", _sanitizer.Sanitize("<p><br></p><p>a</p>"));
}
