using System.Globalization;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Options;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Core.Services;
using SaronidaSailing.Web.Content;
using SaronidaSailing.Web.Localization;

namespace SaronidaSailing.Web.TagHelpers;

[HtmlTargetElement("cms-seo", TagStructure = TagStructure.WithoutEndTag)]
public sealed class CmsSeoTagHelper(
    IPageContentAccessor accessor,
    ILocalizedUrlService urlService,
    IOptions<SiteOptions> site,
    ILogger<CmsSeoTagHelper> logger) : TagHelper
{
    private const string SiteName = "Saronida Sailing";

    private const int MaxOgImageWidth = 1600;

    [HtmlAttributeName("fallback-title")]
    public string? FallbackTitle { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = null;
        output.TagMode = TagMode.StartTagAndEndTag;

        var page = urlService.GetCurrentPage();
        var fallback = string.IsNullOrWhiteSpace(FallbackTitle) ? SiteName : FallbackTitle;

        if (page is null)
        {
            Append(output, Title(fallback));
            return;
        }

        var content = await accessor.GetAsync();
        var title = ReadText(content, $"{page.Key}.seo.title") ?? fallback;
        var description = ReadText(content, $"{page.Key}.seo.description");
        var locale = content.Locale;

        Append(output, Title(title));
        if (description is not null)
        {
            Append(output, Meta("name", "description", description));
        }

        Append(output, Meta("property", "og:title", title));
        if (description is not null)
        {
            Append(output, Meta("property", "og:description", description));
        }

        Append(output, Meta("property", "og:type", "website"));
        Append(output, Meta("property", "og:site_name", SiteName));
        Append(output, Meta("property", "og:url", site.Value.BaseUrl + urlService.GetPath(page.Key, locale)));
        Append(output, Meta("property", "og:locale", OgLocale(locale)));
        Append(output, Meta("property", "og:locale:alternate", OgLocale(OtherLocale(locale))));

        var greekUrl = site.Value.BaseUrl + urlService.GetPath(page.Key, ContentLocales.Greek);
        var englishUrl = site.Value.BaseUrl + urlService.GetPath(page.Key, ContentLocales.English);
        Append(output, Link("canonical", null, locale == ContentLocales.English ? englishUrl : greekUrl));
        Append(output, Link("alternate", ContentLocales.Greek, greekUrl));
        Append(output, Link("alternate", ContentLocales.English, englishUrl));
        Append(output, Link("alternate", "x-default", greekUrl));

        if (FindImage(content, $"{page.Key}.seo.image") is { } image)
        {
            var source = image.Sources.Where(s => s.Width <= MaxOgImageWidth).MaxBy(s => s.Width)
                ?? image.Sources.MinBy(s => s.Width)!;
            Append(output, Meta("property", "og:image", site.Value.BaseUrl + source.Url));
            Append(output, Meta("property", "og:image:width", source.Width.ToString(CultureInfo.InvariantCulture)));
            Append(
                output,
                Meta("property", "og:image:height", image.HeightAt(source.Width).ToString(CultureInfo.InvariantCulture)));
            if (ReadText(content, $"{page.Key}.seo.image-alt") is { } alt)
            {
                Append(output, Meta("property", "og:image:alt", alt));
            }
        }
    }

    private PageImage? FindImage(PageContent content, string key)
    {
        var value = content.GetField(key);
        if (value is null || string.IsNullOrWhiteSpace(value.Value))
        {
            return null;
        }

        if (value.Type != ContentType.Image)
        {
            logger.LogWarning("Content '{Key}' is {Type} but SEO renders it as an image.", key, value.Type);
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

    private string? ReadText(PageContent content, string key)
    {
        var value = content.GetField(key);
        if (value is null)
        {
            return null;
        }

        if (value.Type != ContentType.Text)
        {
            logger.LogWarning("Content '{Key}' is {Type} but SEO renders it as text.", key, value.Type);
            return null;
        }

        return string.IsNullOrWhiteSpace(value.Value) ? null : value.Value;
    }

    private static string OtherLocale(string locale) =>
        locale == ContentLocales.English ? ContentLocales.Greek : ContentLocales.English;

    private static string OgLocale(string locale) => locale == ContentLocales.English ? "en_GB" : "el_GR";

    private static IHtmlContent Title(string text)
    {
        var tag = new TagBuilder("title");
        tag.InnerHtml.Append(text);
        return tag;
    }

    private static IHtmlContent Meta(string attribute, string name, string content)
    {
        var tag = new HtmlContentBuilder();
        tag.AppendHtml($"<meta {attribute}=\"");
        tag.Append(name);
        tag.AppendHtml("\" content=\"");
        tag.Append(content);
        tag.AppendHtml("\" />");
        return tag;
    }

    private static IHtmlContent Link(string rel, string? hreflang, string href)
    {
        var tag = new HtmlContentBuilder();
        tag.AppendHtml($"<link rel=\"{rel}\"");
        if (hreflang is not null)
        {
            tag.AppendHtml($" hreflang=\"{hreflang}\"");
        }

        tag.AppendHtml(" href=\"");
        tag.Append(href);
        tag.AppendHtml("\" />");
        return tag;
    }

    private static void Append(TagHelperOutput output, IHtmlContent tag)
    {
        if (output.Content.GetContent().Length > 0)
        {
            output.Content.AppendHtml("\n    ");
        }

        output.Content.AppendHtml(tag);
    }
}
