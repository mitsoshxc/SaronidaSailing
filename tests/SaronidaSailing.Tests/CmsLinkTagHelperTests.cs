using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Web.TagHelpers;
using static SaronidaSailing.Tests.TagHelperTestSupport;

namespace SaronidaSailing.Tests;

public sealed class CmsLinkTagHelperTests
{
    private static async Task<string> Render(PageContent content, string key = "home.hero.cta")
    {
        var helper = new CmsLinkTagHelper(new FakePageContentAccessor(content), NullLogger<CmsLinkTagHelper>.Instance)
        {
            Key = key,
        };
        var attributes = new TagHelperAttributeList { { "class", "btn" }, { "href", "#template" } };
        var context = NewContext("a", attributes);
        var output = NewOutput("a", attributes);

        await helper.ProcessAsync(context, output);

        return TagHelperTestSupport.Render(output);
    }

    private static PageContent With(string value, string locale = "el", ContentType type = ContentType.Link) =>
        Content(locale, new() { ["home.hero.cta"] = new ContentValue(value, type, false) });

    [Fact]
    public async Task Https_OpensInNewTab_WithGreekNotice()
    {
        var html = await Render(With("""{"url":"https://book.example/x?a=1&b=2","label":"Book <now>"}"""));

        Assert.Equal(
            "<a class=\"btn\" href=\"https://book.example/x?a=1&amp;b=2\" target=\"_blank\" rel=\"noopener\">"
            + "Book &lt;now&gt;<span class=\"visually-hidden\"> (ανοίγει σε νέα καρτέλα)</span></a>",
            html);
    }

    [Fact]
    public async Task Https_EnglishNotice()
    {
        var html = await Render(With("""{"url":"https://book.example","label":"Book"}""", "en"));

        Assert.Contains("<span class=\"visually-hidden\"> (opens in a new tab)</span>", html);
        Assert.Contains("target=\"_blank\"", html);
    }

    [Theory]
    [InlineData("tel:+302291000000")]
    [InlineData("mailto:info@example.com")]
    public async Task TelAndMailto_SameTab_NoNotice(string url)
    {
        var html = await Render(With($$"""{"url":"{{url}}","label":"Call"}"""));

        Assert.Equal($"<a class=\"btn\" href=\"{url.Replace("+", "&#x2B;")}\">Call</a>", html);
    }

    [Theory]
    [InlineData("""{"url":"http://example.com","label":"x"}""")]
    [InlineData("""{"url":"javascript:alert(1)","label":"x"}""")]
    [InlineData("not json")]
    public async Task InvalidLink_IsSuppressed(string value)
    {
        Assert.Equal("", await Render(With(value)));
    }

    [Fact]
    public async Task WrongType_IsSuppressed()
    {
        Assert.Equal("", await Render(With("Hello", type: ContentType.Text)));
    }

    [Fact]
    public async Task Missing_IsSuppressed()
    {
        Assert.Equal("", await Render(Content()));
    }

    [Fact]
    public async Task InvalidKey_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Render(Content(), "home.cta"));
    }

    [Fact]
    public async Task LinkField_OutsideList_Throws()
    {
        var helper = new CmsLinkTagHelper(new FakePageContentAccessor(Content()), NullLogger<CmsLinkTagHelper>.Instance)
        {
            ItemField = "link",
        };
        var attributes = new TagHelperAttributeList();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => helper.ProcessAsync(NewContext("a", attributes), NewOutput("a", attributes)));
    }
}
