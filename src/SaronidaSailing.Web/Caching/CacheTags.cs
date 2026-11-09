namespace SaronidaSailing.Web.Caching;

public static class CacheTags
{
    public const string AllPages = "pages:all";

    public static string Page(string pageKey) => $"page:{pageKey}";
}
