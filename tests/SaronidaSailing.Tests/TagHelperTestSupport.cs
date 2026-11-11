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

    public static readonly ICmsEditContext Visitor = new FakeEditContext(false);

    public static readonly ICmsEditContext Editor = new FakeEditContext(true);

    public static PageContent Content(
        string locale = "el",
        Dictionary<string, ContentValue>? fields = null,
        Dictionary<string, IReadOnlyList<ContentListEntry>>? lists = null,
        Dictionary<string, bool>? sections = null,
        Dictionary<Guid, PageImage>? images = null) =>
        new("home", locale, fields ?? [], lists ?? [], sections ?? [], images);

    public static ContentValue Text(string value) => new(value, ContentType.Text, false);

    public static ContentValue Rich(string value) => new(value, ContentType.RichText, false);

    public static ContentValue Link(string json) => new(json, ContentType.Link, false);

    public static ContentValue Image(Guid id, bool fallback = false) => new(id.ToString(), ContentType.Image, fallback);

    public static PageImage Photo(Guid id, int width = 3000, int height = 2000, params int[] widths) =>
        PhotoOf(id, width, height, widths.Length == 0 ? [480, 960, 1600, 2400] : widths);

    private static PageImage PhotoOf(Guid id, int width, int height, int[] widths) =>
        new(
            id,
            width,
            height,
            [.. widths.Select(w => new PageImageSource(w, $"/media/photo-{w}.webp"))]);

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

internal sealed class FakeEditContext(bool isEditor) : ICmsEditContext
{
    public Task<bool> IsEditorAsync() => Task.FromResult(isEditor);
}

internal sealed class ListLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
{
    public List<string> Warnings { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

    public void Log<TState>(
        Microsoft.Extensions.Logging.LogLevel logLevel,
        Microsoft.Extensions.Logging.EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (logLevel == Microsoft.Extensions.Logging.LogLevel.Warning)
        {
            Warnings.Add(formatter(state, exception));
        }
    }
}
