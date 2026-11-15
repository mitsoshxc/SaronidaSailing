namespace SaronidaSailing.Web.Endpoints;

public static class RobotsTxt
{
    public const string PreviewText = "User-agent: *\nDisallow: /\n";

    public static string Default(string baseUrl) =>
        $"User-agent: *\nDisallow: /admin\n\nSitemap: {baseUrl}{SeoEndpoints.SitemapPath}\n";

    public static string Served(bool isPreview, string? stored, string baseUrl) =>
        isPreview ? PreviewText : stored ?? Default(baseUrl);
}
