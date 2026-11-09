using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Web.TagHelpers;
using static SaronidaSailing.Tests.TagHelperTestSupport;

namespace SaronidaSailing.Tests;

public sealed class CmsTagHelperTests
{
    private static CmsTagHelper Helper(PageContent content, string? key = null, string? field = null) =>
        new(new FakePageContentAccessor(content), Visitor, NullLogger<CmsTagHelper>.Instance) { Key = key, ItemField = field };

    private static async Task<TagHelperOutput> Run(CmsTagHelper helper, params (string, object)[] attributes)
    {
        var list = new TagHelperAttributeList(attributes.Select(a => new TagHelperAttribute(a.Item1, a.Item2)));
        var context = NewContext("h1", list);
        var output = NewOutput("h1", list);
        await helper.ProcessAsync(context, output);
        return output;
    }

    [Fact]
    public async Task Text_IsHtmlEncoded()
    {
        var content = Content(fields: new() { ["home.hero.title"] = Text("<b>Hi & bye</b>") });

        var output = await Run(Helper(content, "home.hero.title"));

        Assert.Equal("<h1>&lt;b&gt;Hi &amp; bye&lt;/b&gt;</h1>", Render(output));
    }

    [Fact]
    public async Task RichText_IsRendered_Raw()
    {
        var content = Content(fields: new() { ["home.hero.body"] = Rich("<p>Hi <strong>there</strong></p>") });

        var output = await Run(Helper(content, "home.hero.body"), ("cms-rich", ""));

        Assert.Equal("<h1><p>Hi <strong>there</strong></p></h1>", Render(output));
    }

    [Fact]
    public async Task MissingValue_IsSuppressed()
    {
        var output = await Run(Helper(Content(), "home.hero.title"));

        Assert.Equal("", Render(output));
    }

    [Theory]
    [InlineData(ContentType.Link)]
    [InlineData(ContentType.Image)]
    public async Task WrongType_IsSuppressed(ContentType type)
    {
        var content = Content(fields: new() { ["home.hero.title"] = new ContentValue("x", type, false) });

        var output = await Run(Helper(content, "home.hero.title"));

        Assert.Equal("", Render(output));
    }

    [Fact]
    public async Task OtherAttributes_AreKept_AndOwnAttributeIsNotEmitted()
    {
        var content = Content(fields: new() { ["home.hero.title"] = Text("Hi") });
        var attributes = new TagHelperAttributeList { { "class", "big" }, { "aria-label", "x" } };
        var context = NewContext("h1", attributes);
        var output = NewOutput("h1", attributes);

        await Helper(content, "home.hero.title").ProcessAsync(context, output);

        Assert.Equal("<h1 class=\"big\" aria-label=\"x\">Hi</h1>", Render(output));
    }

    [Fact]
    public async Task CmsField_OutsideList_Throws()
    {
        var content = Content();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(Helper(content, field: "title")));
    }

    [Fact]
    public async Task BothAttributes_Throws()
    {
        var content = Content();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(Helper(content, "home.hero.title", "title")));
    }

    [Theory]
    [InlineData("home.hero")]
    [InlineData("home.hero.title.extra")]
    [InlineData("Home.hero.title")]
    [InlineData("")]
    [InlineData(null)]
    public async Task InvalidKey_Throws(string? key)
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Run(Helper(Content(), key ?? "")));

        Assert.Contains(key ?? "", ex.Message);
    }

    [Theory]
    [InlineData("a.b")]
    [InlineData("Title")]
    [InlineData("")]
    public async Task InvalidItemField_Throws(string field)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(Helper(Content(), field: field)));
    }
}
