using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Web.Content;
using SaronidaSailing.Web.TagHelpers;
using static SaronidaSailing.Tests.TagHelperTestSupport;

namespace SaronidaSailing.Tests;

public sealed class CmsEditorTagHelperTests
{
    private const string Cta = """{"url":"tel:+30","label":"Call"}""";

    private static async Task<string> RenderText(
        PageContent content,
        ICmsEditContext edit,
        bool rich = false,
        ILogger<CmsTagHelper>? logger = null)
    {
        var attributes = new TagHelperAttributeList { { "class", "t" } };
        if (rich)
        {
            attributes.Add(new TagHelperAttribute("cms-rich"));
        }

        var output = NewOutput("h1", attributes);
        await new CmsTagHelper(new FakePageContentAccessor(content), edit, logger ?? NullLogger<CmsTagHelper>.Instance)
            { Key = "home.hero.title" }.ProcessAsync(NewContext("h1", attributes), output);
        return Render(output);
    }

    private static async Task<string> RenderLink(
        PageContent content,
        ICmsEditContext edit,
        ILogger<CmsLinkTagHelper>? logger = null)
    {
        var attributes = new TagHelperAttributeList { { "class", "b" }, { "href", "#template" }, { "target", "_blank" } };
        var output = NewOutput("a", attributes);
        await new CmsLinkTagHelper(
                new FakePageContentAccessor(content), edit, logger ?? NullLogger<CmsLinkTagHelper>.Instance)
            { Key = "home.hero.cta" }.ProcessAsync(NewContext("a", attributes), output);
        return Render(output);
    }

    [Fact]
    public async Task Text_Editor_HasKeyAndType()
    {
        var content = Content(fields: new() { ["home.hero.title"] = Text("Hi") });

        Assert.Equal(
            "<h1 class=\"t\" data-cms-key=\"home.hero.title\" data-cms-type=\"text\">Hi</h1>",
            await RenderText(content, Editor));
    }

    [Fact]
    public async Task RichText_Editor_HasRichType_AndCmsRichIsRemoved()
    {
        var content = Content(fields: new() { ["home.hero.title"] = Rich("<p>Hi</p>") });

        Assert.Equal(
            "<h1 class=\"t\" data-cms-key=\"home.hero.title\" data-cms-type=\"richText\""
            + " data-cms-value=\"&lt;p&gt;Hi&lt;/p&gt;\"><p>Hi</p></h1>",
            await RenderText(content, Editor, rich: true));
        Assert.Equal("<h1 class=\"t\"><p>Hi</p></h1>", await RenderText(content, Visitor, rich: true));
    }

    [Fact]
    public async Task RichText_Editor_BlankHasNoValueAttribute_TextNeverHasOne()
    {
        var blank = Content(fields: new() { ["home.hero.title"] = Rich("  ") });
        var text = Content(fields: new() { ["home.hero.title"] = Text("Hi") });

        Assert.DoesNotContain("data-cms-value", await RenderText(blank, Editor, rich: true));
        Assert.DoesNotContain("data-cms-value", await RenderText(text, Editor));
    }

    [Fact]
    public async Task Text_Fallback_IsFlagged_ForEditorOnly()
    {
        var content = Content("en", new() { ["home.hero.title"] = new ContentValue("Γεια", ContentType.Text, true) });

        Assert.Equal(
            "<h1 class=\"t\" data-cms-key=\"home.hero.title\" data-cms-type=\"text\" data-cms-fallback>Γεια</h1>",
            await RenderText(content, Editor));
        Assert.Equal("<h1 class=\"t\">Γεια</h1>", await RenderText(content, Visitor));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Text_Blank_IsMissing_ForVisitors_AndPlaceholder_ForEditor(string blank)
    {
        var content = Content(fields: new() { ["home.hero.title"] = Text(blank) });

        Assert.Equal("", await RenderText(content, Visitor));
        Assert.Equal(
            "<h1 class=\"t\" data-cms-key=\"home.hero.title\" data-cms-type=\"text\" data-cms-empty></h1>",
            await RenderText(content, Editor));
    }

    [Fact]
    public async Task Text_Missing_Editor_GetsPlaceholder()
    {
        Assert.Equal(
            "<h1 class=\"t\" data-cms-key=\"home.hero.title\" data-cms-type=\"text\" data-cms-empty></h1>",
            await RenderText(Content(), Editor));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Text_TypeMismatch_IsOmitted_ForEditorToo_WithWarning(bool editor)
    {
        var logger = new ListLogger<CmsTagHelper>();
        var rich = Content(fields: new() { ["home.hero.title"] = Rich("<p>x</p>") });
        var text = Content(fields: new() { ["home.hero.title"] = Text("x") });
        var edit = editor ? Editor : Visitor;

        Assert.Equal("", await RenderText(rich, edit, logger: logger));
        Assert.Equal("", await RenderText(text, edit, rich: true, logger: logger));
        Assert.Equal(2, logger.Warnings.Count);
    }

    [Fact]
    public async Task Link_Valid_Editor_HasKeyTypeAndValue()
    {
        var content = Content(fields: new() { ["home.hero.cta"] = Link(Cta) });

        Assert.Equal(
            "<a class=\"b\" href=\"tel:&#x2B;30\" target=\"_blank\" data-cms-key=\"home.hero.cta\" data-cms-type=\"link\""
            + " data-cms-value=\"{&quot;url&quot;:&quot;tel:&#x2B;30&quot;,&quot;label&quot;:&quot;Call&quot;}\">Call</a>",
            await RenderLink(content, Editor));
    }

    [Fact]
    public async Task Link_Blank_OmittedForVisitor_WithoutWarning()
    {
        var logger = new ListLogger<CmsLinkTagHelper>();
        var content = Content(fields: new() { ["home.hero.cta"] = Link("  ") });

        Assert.Equal("", await RenderLink(content, Visitor, logger));
        Assert.Equal("", await RenderLink(Content(), Visitor, logger));
        Assert.Empty(logger.Warnings);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task Link_BlankOrMissing_Editor_GetsEmptyAnchor(string? blank)
    {
        var content = blank is null ? Content() : Content(fields: new() { ["home.hero.cta"] = Link(blank) });

        Assert.Equal(
            "<a class=\"b\" data-cms-key=\"home.hero.cta\" data-cms-type=\"link\" data-cms-empty></a>",
            await RenderLink(content, Editor));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"url":"http://example.com","label":"x"}""")]
    public async Task Link_Invalid_Editor_GetsInvalidFlag_AndWarns_VisitorOmitted(string json)
    {
        var logger = new ListLogger<CmsLinkTagHelper>();
        var content = Content(fields: new() { ["home.hero.cta"] = Link(json) });

        Assert.Equal(
            "<a class=\"b\" data-cms-key=\"home.hero.cta\" data-cms-type=\"link\""
            + $" data-cms-value=\"{HtmlEncoder.Default.Encode(json)}\" data-cms-invalid></a>",
            await RenderLink(content, Editor, logger));
        Assert.Equal("", await RenderLink(content, Visitor, logger));
        Assert.Equal(2, logger.Warnings.Count);
    }

    [Fact]
    public async Task Link_Fallback_IsFlagged()
    {
        var content = Content("en", new() { ["home.hero.cta"] = new ContentValue(Cta, ContentType.Link, true) });

        Assert.Contains(" data-cms-fallback", await RenderLink(content, Editor));
        Assert.DoesNotContain("data-cms", await RenderLink(content, Visitor));
    }

    [Fact]
    public async Task Link_WrongType_IsOmitted_ForEditor()
    {
        var content = Content(fields: new() { ["home.hero.cta"] = Text("x") });

        Assert.Equal("", await RenderLink(content, Editor));
    }

    private static async Task<string> RenderList(PageContent content, ICmsEditContext edit)
    {
        var accessor = new FakePageContentAccessor(content);
        var attributes = new TagHelperAttributeList { { "class", "item" } };
        var context = NewContext("li", attributes);
        var output = NewOutput("li", attributes, async () =>
        {
            var childAttributes = new TagHelperAttributeList();
            var childOutput = NewOutput("span", childAttributes);
            await new CmsTagHelper(accessor, edit, NullLogger<CmsTagHelper>.Instance) { ItemField = "title" }
                .ProcessAsync(NewContext("span", childAttributes, new Dictionary<object, object>(context.Items)), childOutput);
            return Html(Render(childOutput));
        });

        await new CmsListTagHelper(accessor, edit) { Key = "home.highlights", AddLabel = "Add item" }.ProcessAsync(context, output);
        return Render(output);
    }

    [Fact]
    public async Task List_Editor_RendersAllEntries_InOrder_WithItemAttributes_AndTemplate()
    {
        var first = Entry(1, true, ("title", Text("First")));
        var hidden = Entry(2, false, ("title", Text("Hidden")));
        var content = Content(lists: new() { ["home.highlights"] = new[] { hidden, first } });

        var html = await RenderList(content, Editor);

        Assert.Equal(
            $"<li class=\"item\" data-cms-list=\"home.highlights\" data-cms-item=\"{first.Id}\">"
            + "<span data-cms-key=\"home.highlights.title\" data-cms-type=\"text\">First</span></li>"
            + $"<li class=\"item\" data-cms-list=\"home.highlights\" data-cms-item=\"{hidden.Id}\" data-cms-active=\"false\">"
            + "<span data-cms-key=\"home.highlights.title\" data-cms-type=\"text\">Hidden</span></li>"
            + "<template data-cms-list-template=\"home.highlights\" data-cms-add-label=\"Add item\">"
            + "<li class=\"item\" data-cms-list=\"home.highlights\">"
            + "<span data-cms-key=\"home.highlights.title\" data-cms-type=\"text\" data-cms-empty></span></li></template>",
            html);
    }

    [Fact]
    public async Task List_Editor_WithoutEntries_RendersOnlyTemplate()
    {
        var html = await RenderList(Content(), Editor);

        Assert.StartsWith("<template data-cms-list-template=\"home.highlights\" data-cms-add-label=\"Add item\">", html);
        Assert.Equal(1, html.Split("<li ").Length - 1);
        Assert.DoesNotContain("data-cms-item", html);
    }

    [Fact]
    public async Task List_Visitor_DoesNotRenderInactiveOrTemplate()
    {
        var content = Content(lists: new()
        {
            ["home.highlights"] = new[] { Entry(1, false, ("title", Text("Hidden"))) },
        });

        Assert.Equal("", await RenderList(content, Visitor));
    }

    private static async Task<string> RenderSection(PageContent content, ICmsEditContext edit)
    {
        var attributes = new TagHelperAttributeList { { "id", "ig" } };
        var output = NewOutput("section", attributes);
        await new CmsSectionTagHelper(new FakePageContentAccessor(content), edit) { Key = "home.instagram", Label = "Instagram" }
            .ProcessAsync(NewContext("section", attributes), output);
        return Render(output);
    }

    [Fact]
    public async Task Section_Editor_BlankLabel_Throws()
    {
        var attributes = new TagHelperAttributeList();
        var output = NewOutput("section", attributes);
        var helper = new CmsSectionTagHelper(new FakePageContentAccessor(Content()), Editor)
        {
            Key = "home.instagram",
            Label = " ",
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => helper.ProcessAsync(NewContext("section", attributes), output));
    }

    [Fact]
    public async Task Section_Editor_IsNeverSuppressed()
    {
        var inactive = Content(sections: new() { ["home.instagram"] = false });

        Assert.Equal(
            "<section id=\"ig\" data-cms-section=\"home.instagram\" data-cms-section-label=\"Instagram\" data-cms-active=\"false\"></section>",
            await RenderSection(inactive, Editor));
        Assert.Equal(
            "<section id=\"ig\" data-cms-section=\"home.instagram\" data-cms-section-label=\"Instagram\"></section>",
            await RenderSection(Content(), Editor));
    }
}
