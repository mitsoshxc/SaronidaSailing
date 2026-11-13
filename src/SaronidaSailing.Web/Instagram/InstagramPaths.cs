namespace SaronidaSailing.Web.Instagram;

public static class InstagramPaths
{
    public const string Admin = "/admin/instagram";
    public const string Callback = "/admin/instagram/callback";

    public static string RedirectUri(string siteBaseUrl) => siteBaseUrl.TrimEnd('/') + Callback;

    public static string ResultUrl(string result) => $"{Admin}?result={result}";
}
