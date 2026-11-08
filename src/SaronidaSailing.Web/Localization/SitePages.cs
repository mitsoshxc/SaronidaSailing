namespace SaronidaSailing.Web.Localization;

public sealed record SitePage(string Key, string PagePath, string GreekSlug, string EnglishSlug);

public static class SitePages
{
    public static IReadOnlyList<SitePage> All { get; } =
    [
        new("home", "/Index", "", ""),
        new("trips", "/Trips", "ekdromes", "trips"),
        new("boat", "/Boat", "skafos", "boat"),
        new("contact", "/Contact", "epikoinonia", "contact"),
        new("terms", "/Terms", "oroi", "terms"),
        new("privacy", "/Privacy", "aporrito", "privacy"),
        new("accessibility", "/Accessibility", "prosvasimotita", "accessibility"),
    ];

    public static SitePage? FindByKey(string key) =>
        All.FirstOrDefault(p => p.Key == key);

    public static SitePage? FindByPagePath(string pagePath) =>
        All.FirstOrDefault(p => string.Equals(p.PagePath, pagePath, StringComparison.OrdinalIgnoreCase));
}
