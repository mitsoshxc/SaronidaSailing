using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Razor.TagHelpers;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Core.Services;
using SaronidaSailing.Web.Content;

namespace SaronidaSailing.Web.TagHelpers;

[HtmlTargetElement("cms-image", TagStructure = TagStructure.WithoutEndTag)]
public sealed partial class CmsImageTagHelper(
    IPageContentAccessor accessor,
    ICmsEditContext editContext,
    ILogger<CmsImageTagHelper> logger) : TagHelper
{
    private const int PreferredWidth = 960;

    private const string PlaceholderClass = "cms-image-placeholder";

    [HtmlAttributeName("key")]
    public string? Key { get; set; }

    [HtmlAttributeName("field")]
    public string? ItemField { get; set; }

    [HtmlAttributeName("sizes")]
    public string? Sizes { get; set; }

    [HtmlAttributeName("alt-source")]
    public string? AltSource { get; set; }

    [HtmlAttributeName("loading")]
    public string Loading { get; set; } = "lazy";

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        if (string.IsNullOrWhiteSpace(Sizes))
        {
            throw new InvalidOperationException("'sizes' is required on 'cms-image'.");
        }

        if (Loading is not ("lazy" or "eager"))
        {
            throw new InvalidOperationException($"'loading' must be 'lazy' or 'eager', not '{Loading}'.");
        }

        var field = await CmsFieldResolver.ReadAsync(context, accessor, Key, ItemField, "key", "field");
        var alt = await CmsFieldResolver.ReadAsync(
            context,
            accessor,
            Key is null ? null : Key + "-alt",
            ItemField is null ? null : ItemField + "-alt",
            "key",
            "field");

        var editor = await editContext.IsEditorAsync();
        var value = field.Value;

        if (value is not null && value.Type != ContentType.Image)
        {
            logger.LogWarning(
                "Content '{Key}' is {Type} but the template renders it as an image.", field.Key, value.Type);
            output.SuppressOutput();
            return;
        }

        var content = await accessor.GetAsync();
        var image = FindImage(content, field.Key, value);
        if (image is null && !editor)
        {
            output.SuppressOutput();
            return;
        }

        var altValue = alt.Value;
        if (altValue is not null && altValue.Type != ContentType.Text)
        {
            logger.LogWarning(
                "Content '{Key}' is {Type} but image alt text must be Text.", alt.Key, altValue.Type);
            altValue = null;
        }

        var shownAlt = altValue?.Value ?? string.Empty;
        var inLocale = altValue is { IsFallback: false } ? altValue.Value : string.Empty;
        var greekAlt = content.Locale == ContentLocales.Default || altValue is null or { IsFallback: true } ? shownAlt : null;

        var attributes = output.Attributes;
        if (image is null)
        {
            var layoutClass = attributes["class"]?.Value?.ToString();
            attributes.Clear();
            output.TagName = "button";
            output.TagMode = TagMode.StartTagAndEndTag;
            attributes.SetAttribute("type", "button");
            attributes.SetAttribute(
                "class", string.IsNullOrWhiteSpace(layoutClass) ? PlaceholderClass : $"{layoutClass} {PlaceholderClass}");
            output.Content.SetContent("Add image");
        }
        else
        {
            var source = image.Sources.FirstOrDefault(s => s.Width == PreferredWidth) ?? image.Sources[^1];
            output.TagName = "img";
            output.TagMode = TagMode.StartTagOnly;
            attributes.SetAttribute("src", source.Url);
            attributes.SetAttribute("srcset", string.Join(", ", image.Sources.Select(s => $"{s.Url} {s.Width}w")));
            attributes.SetAttribute("sizes", Sizes);
            attributes.SetAttribute("width", image.Width);
            attributes.SetAttribute("height", image.Height);
            attributes.SetAttribute("alt", shownAlt);
            attributes.SetAttribute("loading", Loading);
            attributes.SetAttribute("decoding", "async");
        }

        if (!editor)
        {
            return;
        }

        attributes.SetAttribute("data-cms-key", field.Key);
        attributes.SetAttribute("data-cms-type", "image");
        attributes.SetAttribute("data-cms-value", image is null ? string.Empty : image.Id.ToString());
        attributes.SetAttribute("data-cms-alt-key", alt.Key);
        attributes.SetAttribute("data-cms-alt", inLocale);
        if (greekAlt is not null)
        {
            attributes.SetAttribute("data-cms-alt-el", greekAlt);
        }

        if (altValue is { IsFallback: true })
        {
            CmsFieldResolver.Flag(output, "data-cms-fallback");
        }

        if (AltSource is not null)
        {
            attributes.SetAttribute("data-cms-alt-proposal", await ReadAltProposalAsync(context));
        }

        if (image is null)
        {
            CmsFieldResolver.Flag(output, "data-cms-empty");
        }
    }

    private PageImage? FindImage(PageContent content, string key, ContentValue? value)
    {
        if (value is null || string.IsNullOrWhiteSpace(value.Value))
        {
            return null;
        }

        var image = Guid.TryParse(value.Value, out var id) ? content.GetImage(id) : null;
        if (image is null || image.Sources.Count == 0)
        {
            logger.LogWarning("Content '{Key}' refers to image '{Id}', which does not exist.", key, value.Value);
            return null;
        }

        return image;
    }

    private async Task<string> ReadAltProposalAsync(TagHelperContext context)
    {
        var source = await CmsFieldResolver.ReadAsync(
            context,
            accessor,
            Key is null ? null : AltSource,
            Key is null ? AltSource : null,
            "alt-source",
            "alt-source");

        return source.Value?.Type switch
        {
            ContentType.Text => source.Value.Value.Trim(),
            ContentType.RichText => ToPlainText(source.Value.Value),
            _ => string.Empty,
        };
    }

    private static string ToPlainText(string html)
    {
        var spaced = BlockEndRegex().Replace(html, " ");
        var stripped = TagRegex().Replace(spaced, string.Empty);
        return WhitespaceRegex().Replace(WebUtility.HtmlDecode(stripped), " ").Trim();
    }

    [GeneratedRegex(@"</(?:p|li|h3)>|<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockEndRegex();

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
