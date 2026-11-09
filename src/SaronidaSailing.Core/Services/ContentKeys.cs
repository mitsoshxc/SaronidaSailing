using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace SaronidaSailing.Core.Services;

public static partial class ContentKeys
{
    public const string Shared = "shared";

    public const int MaxSegmentLength = 100;

    public static bool IsValidSegment(string? segment) =>
        segment is { Length: > 0 and <= MaxSegmentLength } && SegmentPattern().IsMatch(segment);

    public static bool TryParseField(
        string? key,
        [NotNullWhen(true)] out string? pageKey,
        [NotNullWhen(true)] out string? sectionKey,
        [NotNullWhen(true)] out string? fieldKey)
    {
        pageKey = sectionKey = fieldKey = null;
        var parts = Split(key, 3);
        if (parts is null)
        {
            return false;
        }

        (pageKey, sectionKey, fieldKey) = (parts[0], parts[1], parts[2]);
        return true;
    }

    public static bool TryParseList(
        string? key, [NotNullWhen(true)] out string? pageKey, [NotNullWhen(true)] out string? listKey) =>
        TryParsePair(key, out pageKey, out listKey);

    public static bool TryParseSection(
        string? key, [NotNullWhen(true)] out string? pageKey, [NotNullWhen(true)] out string? sectionKey) =>
        TryParsePair(key, out pageKey, out sectionKey);

    private static bool TryParsePair(string? key, out string? first, out string? second)
    {
        first = second = null;
        var parts = Split(key, 2);
        if (parts is null)
        {
            return false;
        }

        (first, second) = (parts[0], parts[1]);
        return true;
    }

    private static string[]? Split(string? key, int count)
    {
        if (key is null)
        {
            return null;
        }

        var parts = key.Split('.');
        return parts.Length == count && parts.All(IsValidSegment) ? parts : null;
    }

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*\z")]
    private static partial Regex SegmentPattern();
}
