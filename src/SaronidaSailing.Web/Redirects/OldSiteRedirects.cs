using SaronidaSailing.Web.Localization;

namespace SaronidaSailing.Web.Redirects;

public sealed record OldSiteTarget(string PageKey, string Culture);

public static class OldSiteRedirects
{
    public const string Root = "/index.php";

    private static readonly OldSiteTarget Home = new("home", SiteCultures.Greek);
    private static readonly OldSiteTarget HomeEn = new("home", SiteCultures.English);

    private static readonly Dictionary<string, OldSiteTarget> Exact = new(StringComparer.OrdinalIgnoreCase)
    {
        [Root] = Home,
        [Root + "/el"] = Home,
        [Root + "/en"] = HomeEn,
        [Root + "/el/yacht-charter-gr"] = new("trips", SiteCultures.Greek),
        [Root + "/el/daily-excursions-gr"] = new("trips", SiteCultures.Greek),
        [Root + "/en/yacht-charter"] = new("trips", SiteCultures.English),
        [Root + "/en/daily-excursions"] = new("trips", SiteCultures.English),
        [Root + "/el/sailing-yacht-2"] = new("boat", SiteCultures.Greek),
        [Root + "/en/sailing-yacht"] = new("boat", SiteCultures.English),
        [Root + "/el/contacts-gr"] = new("contact", SiteCultures.Greek),
        [Root + "/en/contacts"] = new("contact", SiteCultures.English),
        [Root + "/el/privacy-policy"] = new("terms", SiteCultures.Greek),
        [Root + "/en/privacy-policy"] = new("terms", SiteCultures.English),
        [Root + "/el/friends-gr"] = Home,
        [Root + "/en/friends-en"] = HomeEn,
    };

    private static readonly string[] GonePrefixes = [Root + "/el/component/users", Root + "/en/component/users"];

    public static bool IsOldSitePath(PathString path) =>
        path.StartsWithSegments(Root, StringComparison.OrdinalIgnoreCase);

    public static bool IsGone(PathString path) =>
        GonePrefixes.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));

    public static OldSiteTarget Resolve(PathString path)
    {
        var normalized = path.Value!.TrimEnd('/');
        if (Exact.TryGetValue(normalized, out var target))
        {
            return target;
        }

        return path.StartsWithSegments(Root + "/en", StringComparison.OrdinalIgnoreCase) ? HomeEn : Home;
    }
}
