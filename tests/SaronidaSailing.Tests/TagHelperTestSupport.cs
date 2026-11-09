using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.AspNetCore.Razor.TagHelpers;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Web.Content;

namespace SaronidaSailing.Tests;

internal sealed class FakePageContentAccessor(PageContent content) : IPageContentAccessor
{
    public Task<PageContent> GetAsync() => Task.FromResult(content);
}

internal static class TagHelperTestSupport
{
    private static readonly HtmlEncoder Encoder = HtmlEncoder.Create(UnicodeRanges.All);

    public static PageContent Content(
        string locale = "el",
        Dictionary<string, ContentValue>? fields = null,
        Dictionary<string, IReadOnlyList<ContentListEntry>>? lists = null,
        Dictionary<string, bool>? sections = null) =>
        new("home", locale, fields ?? [], lists ?? [], sections ?? []);

    public static ContentValue Text(string value) => new(value, ContentType.Text, false);

    public static ContentValue Rich(string value) => new(value, ContentType.RichText, false);

    public static ContentValue Link(string json) => new(json, ContentType.Link, false);

    public static ContentListEntry Entry(int sortOrder, bool active, params (string Field, ContentValue Value)[] fields) =>
        new(Guid.NewGuid(), sortOrder, active, fields.ToDictionary(f => f.Field, f => f.Value));

    public static TagHelperContext NewContext(
        string tagName, TagHelperAttributeList attributes, IDictionary<object, object>? items = null) =>
        new(tagName, attributes, items ?? new Dictionary<object, object>(), Guid.NewGuid().ToString("N"));

    public static TagHelperOutput NewOutput(
        string tagName,
        TagHelperAttributeList attributes,
        Func<Task<TagHelperContent>>? children = null) =>
        new(tagName, attributes, (_, _) => children?.Invoke() ?? Task.FromResult<TagHelperContent>(Template("template")));

    public static TagHelperContent Template(string text)
    {
        var content = new DefaultTagHelperContent();
        content.SetContent(text);
        return content;
    }

    public static TagHelperContent Html(string html)
    {
        var content = new DefaultTagHelperContent();
        content.SetHtmlContent(html);
        return content;
    }

    public static string Render(TagHelperOutput output)
    {
        using var writer = new StringWriter();
        output.WriteTo(writer, Encoder);
        return writer.ToString();
    }
}
