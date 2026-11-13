using System.Text.RegularExpressions;

namespace SaronidaSailing.Core.Services;

public static partial class InstagramAltText
{
    public const int MaxLength = 125;

    private const string Ellipsis = "…";

    public static string? FromCaption(string? caption)
    {
        var firstLine = caption?
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        if (firstLine is null)
        {
            return null;
        }

        var text = WhitespaceRegex().Replace(firstLine, " ");
        if (text.Length <= MaxLength)
        {
            return text;
        }

        var budget = MaxLength - Ellipsis.Length;
        var cut = text[..budget];
        if (text[budget] != ' ')
        {
            var space = cut.LastIndexOf(' ');
            if (space > 0)
            {
                cut = cut[..space];
            }
            else if (char.IsHighSurrogate(cut[^1]))
            {
                cut = cut[..^1];
            }
        }

        return cut.TrimEnd() + Ellipsis;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
