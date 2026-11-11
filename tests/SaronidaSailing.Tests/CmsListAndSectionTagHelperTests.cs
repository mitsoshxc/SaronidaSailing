using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Web.Content;
using SaronidaSailing.Web.TagHelpers;
using static SaronidaSailing.Tests.TagHelperTestSupport;

namespace SaronidaSailing.Tests;

public sealed class CmsListAndSectionTagHelperTests
{
    private const string TitleLink = """{"url":"tel:+30","label":"Call"}""";

    private static PageContent ListContent(params ContentListEntry[] entries) =>
        Content(lists: new() { ["home.highlights"] = entries });

    private static async Task<string> RenderList(PageContent content, string key = "home.highlights", Func<TagHelperContext, Task<TagHelperContent>>? child = null)
    {
        var accessor = new FakePageContentAccessor(content);
        var attributes = new TagHelperAttributeList { { "class", "item" } };
        var context = NewContext("li", attributes);
        var output = NewOutput("li", attributes, () => (child ?? RenderTitleChild)(context));

        await new CmsListTagHelper(accessor, Visitor) { Key = key, AddLabel = "Add item" }.ProcessAsync(context, output);

        return Render(output);

        async Task<TagHelperContent> RenderTitleChild(TagHelperContext listContext)
        {
            var childAttributes = new TagHelperAttributeList();
            var childContext = NewContext("span", childAttributes, new Dictionary<object, object>(listContext.Items));
            var childOutput = NewOutput("span", childAttributes);
            await new CmsTagHelper(accessor, Visitor, NullLogger<CmsTagHelper>.Instance) { ItemField = "title" }
                .ProcessAsync(childContext, childOutput);
            return Html(Render(childOutput));
        }
    }

    [Fact]
    public async Task List_RepeatsOnlyActiveEntries_InOrder_WithPerEntryFields()
    {
        var content = ListContent(
            Entry(3, true, ("title", Text("Third"))),
            Entry(1, true, ("title", Text("First"))),
            Entry(2, false, ("title", Text("Hidden"))));

        var html = await RenderList(content);

        Assert.Equal(
            "<li class=\"item\"><span>First</span></li><li class=\"item\"><span>Third</span></li>", html);
    }

    [Fact]
    public async Task List_EntryWithoutField_OmitsChildOnly()
    {
        var content = ListContent(Entry(1, true), Entry(2, true, ("title", Text("Two"))));

        var html = await RenderList(content);

        Assert.Equal("<li class=\"item\"></li><li class=\"item\"><span>Two</span></li>", html);
    }

    private static async Task<string> RenderListWithLabel(ICmsEditContext editContext, string? label, params ContentListEntry[] entries)
    {
        var accessor = new FakePageContentAccessor(ListContent(entries));
        var attributes = new TagHelperAttributeList { { "class", "item" } };
        var context = NewContext("li", attributes);
        var output = NewOutput("li", attributes, () => Task.FromResult<TagHelperContent>(Html("")));

        await new CmsListTagHelper(accessor, editContext) { Key = "home.highlights", AddLabel = label }
            .ProcessAsync(context, output);

        return Render(output);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task List_Editor_CarriesAddLabelOnTemplate(bool withItems)
    {
        ContentListEntry[] entries = withItems ? [Entry(1, true)] : [];

        var html = await RenderListWithLabel(Editor, "Add step", entries);

        Assert.Contains("<template data-cms-list-template=\"home.highlights\" data-cms-add-label=\"Add step\">", html);
        Assert.DoesNotContain("cms-add-label=\"Add step\" class", html);
    }

    [Fact]
    public async Task List_Visitor_NeverShowsAddLabel()
    {
        var html = await RenderListWithLabel(Visitor, "Add step", Entry(1, true));

        Assert.DoesNotContain("add-label", html);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task List_MissingOrBlankAddLabel_Throws_ForVisitorAndEditor(string? label)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => RenderListWithLabel(Visitor, label, Entry(1, true)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => RenderListWithLabel(Editor, label, Entry(1, true)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => RenderListWithLabel(Visitor, label));
    }

    [Fact]
    public async Task List_NoActiveEntries_IsSuppressed()
    {
        Assert.Equal("", await RenderList(ListContent(Entry(1, false, ("title", Text("x"))))));
        Assert.Equal("", await RenderList(ListContent()));
    }

    [Fact]
    public async Task List_LinkFieldResolvesAgainstEntry()
    {
        var content = ListContent(Entry(1, true, ("link", Link(TitleLink))));
        var accessor = new FakePageContentAccessor(content);
        var attributes = new TagHelperAttributeList();
        var context = NewContext("li", attributes);
        var output = NewOutput("li", attributes, async () =>
        {
            var childAttributes = new TagHelperAttributeList();
            var childOutput = NewOutput("a", childAttributes);
            await new CmsLinkTagHelper(accessor, Visitor, NullLogger<CmsLinkTagHelper>.Instance) { ItemField = "link" }
                .ProcessAsync(NewContext("a", childAttributes, new Dictionary<object, object>(context.Items)), childOutput);
            return Html(Render(childOutput));
        });

        await new CmsListTagHelper(accessor, Visitor) { Key = "home.highlights", AddLabel = "Add item" }.ProcessAsync(context, output);

        Assert.Equal("<li><a href=\"tel:&#x2B;30\">Call</a></li>", Render(output));
    }

    [Fact]
    public async Task List_Nested_Throws()
    {
        var content = ListContent(Entry(1, true, ("title", Text("x"))));
        var accessor = new FakePageContentAccessor(content);

        await Assert.ThrowsAsync<InvalidOperationException>(() => RenderList(content, child: async outer =>
        {
            var attributes = new TagHelperAttributeList();
            var innerContext = NewContext("ul", attributes, new Dictionary<object, object>(outer.Items));
            await new CmsListTagHelper(accessor, Visitor) { Key = "home.highlights", AddLabel = "Add item" }
                .ProcessAsync(innerContext, NewOutput("ul", attributes));
            return new DefaultTagHelperContent();
        }));
    }

    [Theory]
    [InlineData("home")]
    [InlineData("home.highlights.x")]
    [InlineData("")]
    public async Task List_InvalidKey_Throws(string key)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => RenderList(ListContent(), key));
    }

    private static async Task<string> RenderSection(PageContent content, string key = "home.instagram")
    {
        var attributes = new TagHelperAttributeList { { "id", "ig" } };
        var output = NewOutput("section", attributes);

        await new CmsSectionTagHelper(new FakePageContentAccessor(content), Visitor) { Key = key }
            .ProcessAsync(NewContext("section", attributes), output);

        return Render(output);
    }

    [Fact]
    public async Task Section_Inactive_IsSuppressed()
    {
        var content = Content(sections: new() { ["home.instagram"] = false });

        Assert.Equal("", await RenderSection(content));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Section_ActiveOrUnknown_IsKept(bool explicitlyActive)
    {
        var content = explicitlyActive ? Content(sections: new() { ["home.instagram"] = true }) : Content();

        Assert.Equal("<section id=\"ig\"></section>", await RenderSection(content));
    }

    [Theory]
    [InlineData("home")]
    [InlineData("home.instagram.x")]
    [InlineData("")]
    public async Task Section_InvalidKey_Throws(string key)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => RenderSection(Content(), key));
    }

    private static async Task<string> RenderSectionLink(
        PageContent content, string key, ICmsEditContext editContext)
    {
        var attributes = new TagHelperAttributeList { { "href", "#x" } };
        var output = NewOutput("a", attributes);

        await new CmsSectionLinkTagHelper(new FakePageContentAccessor(content), editContext) { Key = key }
            .ProcessAsync(NewContext("a", attributes), output);

        return Render(output);
    }

    [Fact]
    public async Task SectionLink_VisitorActive_IsRenderedWithoutAttribute()
    {
        var html = await RenderSectionLink(Content(), "home.instagram", Visitor);

        Assert.Equal("<a href=\"#x\"></a>", html);
    }

    [Fact]
    public async Task SectionLink_VisitorInactive_IsSuppressed()
    {
        var content = Content(sections: new() { ["home.instagram"] = false });

        Assert.Equal("", await RenderSectionLink(content, "home.instagram", Visitor));
    }

    [Fact]
    public async Task SectionLink_EditorInactive_HasBothDataAttributes()
    {
        var content = Content(sections: new() { ["home.instagram"] = false });

        var html = await RenderSectionLink(content, "home.instagram", Editor);

        Assert.Equal("<a href=\"#x\" data-cms-section-link=\"home.instagram\" data-cms-active=\"false\"></a>", html);
    }

    [Fact]
    public async Task SectionLink_EditorActive_HasOnlySectionLinkAttribute()
    {
        var html = await RenderSectionLink(Content(), "home.instagram", Editor);

        Assert.Equal("<a href=\"#x\" data-cms-section-link=\"home.instagram\"></a>", html);
    }

    [Fact]
    public async Task SectionLink_SharedKey_WorksOnAPageSnapshot()
    {
        var content = Content(sections: new() { ["shared.sandbox"] = false });

        Assert.Equal("", await RenderSectionLink(content, "shared.sandbox", Visitor));
    }

    [Fact]
    public async Task SectionLink_OtherPageKey_Throws()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => RenderSectionLink(Content(), "boat.gallery", Visitor));

        Assert.Equal(
            "'cms-section-link' can only point to a section of the current page ('home') or 'shared' (got 'boat.gallery').",
            exception.Message);
    }

    [Theory]
    [InlineData("home")]
    [InlineData("home.instagram.x")]
    [InlineData("")]
    public async Task SectionLink_InvalidKey_Throws(string key)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => RenderSectionLink(Content(), key, Visitor));
    }
}
