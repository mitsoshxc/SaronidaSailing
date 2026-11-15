using System.Diagnostics.CodeAnalysis;

namespace SaronidaSailing.Core.Services;

public static class PageRobots
{
    public const string Section = "seo";

    public const string IndexField = "index";

    public const string FollowField = "follow";

    public const string On = "true";

    public const string Off = "false";

    public static bool IsRobotsKey(string key, [NotNullWhen(true)] out string? pageKey)
    {
        pageKey = null;
        if (!ContentKeys.TryParseField(key, out var page, out var section, out var field)
            || section != Section
            || field is not (IndexField or FollowField))
        {
            return false;
        }

        pageKey = page;
        return true;
    }

    public static bool IsValidValue(string? value) => value is On or Off;

    public static bool IsEnabled(string? value) => value != Off;
}
