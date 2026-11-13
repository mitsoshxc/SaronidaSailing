namespace SaronidaSailing.Web.Caching;

public static class CacheTags
{
    public const string PolicyName = "CmsPage";

    public const string TripsPolicyName = "CmsPageTrips";

    public const string AllPages = "pages:all";

    public const string HomePage = "page:home";

    public const string TripsPage = "page:trips";

    public const string BoatPage = "page:boat";

    public const string ContactPage = "page:contact";

    public const string TermsPage = "page:terms";

    public const string PrivacyPage = "page:privacy";

    public const string AccessibilityPage = "page:accessibility";

    public static string Page(string pageKey) => $"page:{pageKey}";
}
