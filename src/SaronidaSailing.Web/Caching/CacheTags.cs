namespace SaronidaSailing.Web.Caching;

public static class CacheTags
{
    public const string PolicyName = "CmsPage";

    public const string AllPages = "pages:all";

    public const string HomePage = "page:home";

    public static string Page(string pageKey) => $"page:{pageKey}";
}
