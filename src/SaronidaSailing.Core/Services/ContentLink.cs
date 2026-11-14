using System.Diagnostics.CodeAnalysis;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace SaronidaSailing.Core.Services;

public sealed record ContentLink(string Url, string Label)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly string[] AllowedSchemes = [Uri.UriSchemeHttps, Uri.UriSchemeMailto, "tel"];

    public static bool TryParse(string? json, [NotNullWhen(true)] out ContentLink? link)
    {
        link = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        ContentLink? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<ContentLink>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return false;
        }

        if (parsed is not { Url: { } url, Label: { } label }
            || string.IsNullOrWhiteSpace(url)
            || string.IsNullOrWhiteSpace(label)
            || !(IsAllowedUrl(url) || IsKnownPageUrl(url)))
        {
            return false;
        }

        link = parsed;
        return true;
    }

    public static string? FindUnknownPageKey(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var url = JsonSerializer.Deserialize<ContentLink>(json, JsonOptions)?.Url;
            return url is not null && SitePageKeys.TryGetKey(url, out var key) && !SitePageKeys.IsKnown(key) ? key : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static bool IsKnownPageUrl(string url) =>
        SitePageKeys.TryGetKey(url, out var key) && SitePageKeys.IsKnown(key);

    public string ToJson() => JsonSerializer.Serialize(this, WriteOptions);

    public static bool IsAllowedUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && AllowedSchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase);
}
