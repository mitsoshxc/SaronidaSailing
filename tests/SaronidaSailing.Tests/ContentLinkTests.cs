using SaronidaSailing.Core.Services;

namespace SaronidaSailing.Tests;

public sealed class ContentLinkTests
{
    [Theory]
    [InlineData("https://example.com/book", "Book")]
    [InlineData("tel:+302291000000", "Call")]
    [InlineData("mailto:info@example.com", "Email")]
    [InlineData("HTTPS://example.com", "Upper")]
    [InlineData("MailTo:info@example.com", "Mixed")]
    public void TryParse_AllowedSchemes_Succeeds(string url, string label)
    {
        var json = $$"""{"url": "{{url}}", "label": "{{label}}"}""";

        Assert.True(ContentLink.TryParse(json, out var link));
        Assert.Equal(new ContentLink(url, label), link);
    }

    [Fact]
    public void TryParse_PropertyNamesAreCaseInsensitive()
    {
        Assert.True(ContentLink.TryParse("""{"URL": "https://example.com", "Label": "x"}""", out var link));
        Assert.Equal("x", link.Label);
    }

    [Theory]
    [InlineData("""{"url": "http://example.com", "label": "x"}""")]
    [InlineData("""{"url": "javascript:alert(1)", "label": "x"}""")]
    [InlineData("""{"url": "/relative", "label": "x"}""")]
    [InlineData("""{"url": "example.com", "label": "x"}""")]
    [InlineData("""{"url": "ftp://example.com", "label": "x"}""")]
    [InlineData("""{"label": "x"}""")]
    [InlineData("""{"url": "https://example.com"}""")]
    [InlineData("""{"url": "  ", "label": "x"}""")]
    [InlineData("""{"url": "https://example.com", "label": " "}""")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_Invalid_Fails(string? json)
    {
        Assert.False(ContentLink.TryParse(json, out var link));
        Assert.Null(link);
    }

    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("tel:+30", true)]
    [InlineData("mailto:a@b.gr", true)]
    [InlineData("TEL:+30", true)]
    [InlineData("http://example.com", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("/path", false)]
    [InlineData("", false)]
    public void IsAllowedUrl_ChecksScheme(string url, bool expected) =>
        Assert.Equal(expected, ContentLink.IsAllowedUrl(url));
}
