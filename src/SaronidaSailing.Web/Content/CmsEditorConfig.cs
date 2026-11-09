using System.Text.Json;
using SaronidaSailing.Core.Interfaces;

namespace SaronidaSailing.Web.Content;

public sealed record CmsEditorConfig(
    string PageKey,
    string Locale,
    string SaveUrl,
    string HistoryUrl,
    string SavesUrl,
    string AntiforgeryToken)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static CmsEditorConfig Create(PageContent content, string antiforgeryToken) => new(
        content.PageKey,
        content.Locale,
        "/admin/api/content",
        "/admin/api/content/history",
        "/admin/api/content/saves/",
        antiforgeryToken);

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
}
