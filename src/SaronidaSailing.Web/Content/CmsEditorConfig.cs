using System.Text.Json;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Core.Services;
using SaronidaSailing.Web.Localization;

namespace SaronidaSailing.Web.Content;

public sealed record CmsSeoValue(string Value, string? Greek);

public sealed record CmsSeoImage(string Value, string PreviewUrl);

public sealed record CmsSeoAlt(string Value, string Greek);

public sealed record CmsSeoConfig(
    string PageKey,
    CmsSeoValue Title,
    CmsSeoValue Description,
    CmsSeoImage Image,
    CmsSeoAlt ImageAlt,
    bool Index,
    bool Follow);

public sealed record CmsSitePage(string Key, string Path, string El, string En);

public sealed record CmsEditorConfig(
    string PageKey,
    string Locale,
    string SaveUrl,
    string HistoryUrl,
    string SavesUrl,
    string AntiforgeryToken,
    string OpensInNewTab,
    CmsSeoConfig? Seo,
    IReadOnlyList<CmsSitePage> Pages)
{
    private const int PreviewWidth = 960;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static CmsEditorConfig Create(PageContent content, string antiforgeryToken, SitePage? page) => new(
        content.PageKey,
        content.Locale,
        "/admin/api/content",
        "/admin/api/content/history",
        "/admin/api/content/saves/",
        antiforgeryToken,
        UiText.OpensInNewTab(content.Locale),
        page is null
            ? null
            : new CmsSeoConfig(
                page.Key,
                ReadSeo(content, $"{page.Key}.seo.title"),
                ReadSeo(content, $"{page.Key}.seo.description"),
                ReadImage(content, $"{page.Key}.seo.image"),
                ReadAlt(content, $"{page.Key}.seo.image-alt"),
                ReadSwitch(content, $"{page.Key}.seo.{PageRobots.IndexField}"),
                ReadSwitch(content, $"{page.Key}.seo.{PageRobots.FollowField}")),
        [.. SitePages.All.Select(p => new CmsSitePage(
            p.Key,
            LocalizedUrlService.PathFor(p.Key, content.Locale),
            UiText.PageTitle(p.Key).Greek,
            UiText.PageTitle(p.Key).English))]);

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    private static bool ReadSwitch(PageContent content, string key)
    {
        var value = content.GetField(key);
        return value is null || value.Type != ContentType.Text || PageRobots.IsEnabled(value.Value);
    }

    private static CmsSeoValue ReadSeo(PageContent content, string key)
    {
        var value = content.GetField(key);
        if (value is null || value.Type != ContentType.Text || string.IsNullOrWhiteSpace(value.Value))
        {
            return new CmsSeoValue("", null);
        }

        if (!value.IsFallback)
        {
            return new CmsSeoValue(value.Value, null);
        }

        return new CmsSeoValue("", content.Locale == ContentLocales.English ? value.Value : null);
    }

    private static CmsSeoImage ReadImage(PageContent content, string key)
    {
        var value = content.GetField(key);
        if (value is null || value.Type != ContentType.Image || string.IsNullOrWhiteSpace(value.Value))
        {
            return new CmsSeoImage("", "");
        }

        var image = Guid.TryParse(value.Value, out var id) ? content.GetImage(id) : null;
        var source = image?.Sources.FirstOrDefault(s => s.Width == PreviewWidth) ?? image?.Sources.LastOrDefault();
        return new CmsSeoImage(value.Value, source?.Url ?? "");
    }

    private static CmsSeoAlt ReadAlt(PageContent content, string key)
    {
        var value = content.GetField(key);
        if (value is null || value.Type != ContentType.Text)
        {
            return new CmsSeoAlt("", "");
        }

        if (content.Locale == ContentLocales.Default)
        {
            return new CmsSeoAlt(value.Value, value.Value);
        }

        return value.IsFallback
            ? new CmsSeoAlt("", value.Value)
            : new CmsSeoAlt(value.Value, value.DefaultLocaleValue ?? "");
    }
}
