namespace SaronidaSailing.Web.Localization;

public static class SiteCultures
{
    public const string Greek = "el";
    public const string English = "en";
    public const string Default = Greek;
    public const string EnglishPrefix = "en";

    public static IReadOnlyList<string> Supported { get; } = [Greek, English];

    public static string ForPath(PathString path) =>
        path.StartsWithSegments($"/{EnglishPrefix}", StringComparison.OrdinalIgnoreCase) ? English : Greek;
}
