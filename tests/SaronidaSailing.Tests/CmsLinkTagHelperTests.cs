using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Web.Content;
using SaronidaSailing.Web.TagHelpers;
using static SaronidaSailing.Tests.TagHelperTestSupport;

namespace SaronidaSailing.Tests;

public sealed class CmsLinkTagHelperTests
{
    private static async Task<string> Render(PageContent content, string key = "home.hero.cta")
    {
        var helper = new CmsLinkTagHelper(new FakePageContentAccessor(content), Visitor, NullLogger<CmsLinkTagHelper>.Instance)
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
        var helper = new CmsLinkTagHelper(new FakePageContentAccessor(Content()), Visitor, NullLogger<CmsLinkTagHelper>.Instance)
        {
            ItemField = "link",
        };
        var attributes = new TagHelperAttributeList();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => helper.ProcessAsync(NewContext("a", attributes), NewOutput("a", attributes)));
    }

    private static async Task<string> RenderKept(
        PageContent content,
        ICmsEditContext editContext,
        string? key = "home.hero.cta",
        string? itemField = null,
        IDictionary<object, object>? items = null,
        string template = "<svg></svg>")
    {
        var helper = new CmsLinkTagHelper(new FakePageContentAccessor(content), editContext, NullLogger<CmsLinkTagHelper>.Instance)
        {
            Key = key,
            ItemField = itemField,
        };
        items ??= new Dictionary<object, object>();
        var contextAttributes = new TagHelperAttributeList { { "class", "icon-link" }, { "cms-keep-content", "" } };
        var outputAttributes = new TagHelperAttributeList { { "class", "icon-link" }, { "cms-keep-content", "" }, { "href", "#template" } };
        var context = NewContext("a", contextAttributes, items);
        var output = NewOutput("a", outputAttributes, async () =>
        {
            var slotAttributes = new TagHelperAttributeList { { "cms-link-label", "" }, { "class", "label" } };
            var slot = NewOutput("span", slotAttributes);
            await new CmsLinkLabelTagHelper(editContext).ProcessAsync(NewContext("span", slotAttributes, items), slot);
            return Html(template + TagHelperTestSupport.Render(slot));
        });

        await helper.ProcessAsync(context, output);

        return TagHelperTestSupport.Render(output);
    }

    [Fact]
    public async Task Keep_Https_FillsSlotKeepsTemplateAndAppendsNotice()
    {
        var html = await RenderKept(
            With("""{"url":"https://example.com/p","label":"Insta <gram>"}"""), Visitor);

        Assert.Equal(
            "<a class=\"icon-link\" href=\"https://example.com/p\" target=\"_blank\" rel=\"noopener\">"
            + "<svg></svg><span class=\"label\">Insta &lt;gram&gt;</span>"
            + "<span class=\"visually-hidden\"> (ανοίγει σε νέα καρτέλα)</span></a>",
            html);
    }

    [Fact]
    public async Task Keep_Tel_NoTargetNoNotice()
    {
        var html = await RenderKept(With("""{"url":"tel:+302291000000","label":"22910 00000"}"""), Visitor);

        Assert.Equal(
            "<a class=\"icon-link\" href=\"tel:&#x2B;302291000000\"><svg></svg><span class=\"label\">22910 00000</span></a>",
            html);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    public async Task Keep_Visitor_BlankOrInvalid_IsSuppressed(string value)
    {
        Assert.Equal("", await RenderKept(With(value), Visitor));
        Assert.Equal("", await RenderKept(Content(), Visitor));
    }

    [Fact]
    public async Task Keep_Editor_Https_FlagsAdminMarkup()
    {
        var html = await RenderKept(With("""{"url":"https://example.com","label":"L"}"""), Editor);

        Assert.Contains("data-cms-keep-content", html);
        Assert.Contains("<span class=\"label\" data-cms-link-label>L</span>", html);
        Assert.EndsWith("<span class=\"visually-hidden\" data-cms-new-tab-notice> (ανοίγει σε νέα καρτέλα)</span></a>", html);
        Assert.DoesNotContain("cms-keep-content=", html);
    }

    [Fact]
    public async Task Keep_Editor_Blank_KeepsTemplateWithEmptySlot()
    {
        var html = await RenderKept(Content(), Editor);

        Assert.Contains("data-cms-empty", html);
        Assert.Contains("data-cms-keep-content", html);
        Assert.Contains("<svg></svg><span class=\"label\" data-cms-link-label></span>", html);
        Assert.DoesNotContain("href", html);
        Assert.DoesNotContain("target", html);
    }

    [Fact]
    public async Task Keep_Editor_InvalidJson_IsFlaggedInvalid()
    {
        var html = await RenderKept(With("not json"), Editor);

        Assert.Contains("data-cms-invalid", html);
        Assert.Contains("<svg></svg>", html);
        Assert.DoesNotContain("href", html);
    }

    [Fact]
    public async Task Keep_NoSlot_KeepsFixedText()
    {
        var html = await RenderKept(
            With("""{"url":"tel:123","label":"ignored"}"""), Visitor, template: "Contact");

        Assert.StartsWith("<a class=\"icon-link\" href=\"tel:123\">Contact<span class=\"label\">ignored</span>", html);
    }

    [Fact]
    public async Task Keep_LinkField_InsideList_Works()
    {
        var content = Content(lists: new()
        {
            ["home.cards"] = [Entry(0, true, ("link", Link("""{"url":"mailto:a@b.gr","label":"Mail"}""")))],
        });
        var accessor = new FakePageContentAccessor(content);
        var attributes = new TagHelperAttributeList();
        var context = NewContext("li", attributes);
        var output = NewOutput("li", attributes, async () =>
            Html(await RenderKept(content, Visitor, key: null, itemField: "link", items: new Dictionary<object, object>(context.Items))));

        await new CmsListTagHelper(accessor, Visitor) { Key = "home.cards", AddLabel = "Add card" }.ProcessAsync(context, output);
        var html = TagHelperTestSupport.Render(output);

        Assert.Contains("href=\"mailto:a@b.gr\"", html);
        Assert.Contains("<span class=\"label\">Mail</span>", html);
    }

    [Fact]
    public async Task LinkLabel_OutsideKeepLink_Throws()
    {
        var attributes = new TagHelperAttributeList { { "cms-link-label", "" } };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new CmsLinkLabelTagHelper(Visitor).ProcessAsync(NewContext("span", attributes), NewOutput("span", attributes)));
    }
}
