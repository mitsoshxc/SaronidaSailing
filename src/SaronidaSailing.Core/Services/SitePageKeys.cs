namespace SaronidaSailing.Core.Services;

public static class SitePageKeys
{
    public const string UrlPrefix = "page:";

    public static IReadOnlyList<string> All { get; } =
        ["home", "trips", "boat", "contact", "terms", "privacy", "accessibility"];

    public static bool IsKnown(string key) => All.Contains(key, StringComparer.Ordinal);

    public static bool TryGetKey(string url, out string key)
    {
        key = url.StartsWith(UrlPrefix, StringComparison.Ordinal) ? url[UrlPrefix.Length..] : "";
        return key.Length > 0;
    }
}
