using System.Globalization;
using System.Text;

namespace SaronidaSailing.Core.Services;

public static class Slugs
{
    private const int MaxLength = 50;
    private const string Fallback = "image";

    private static readonly Dictionary<char, string> GreekLetters = new()
    {
        ['α'] = "a", ['β'] = "v", ['γ'] = "g", ['δ'] = "d", ['ε'] = "e", ['ζ'] = "z",
        ['η'] = "i", ['θ'] = "th", ['ι'] = "i", ['κ'] = "k", ['λ'] = "l", ['μ'] = "m",
        ['ν'] = "n", ['ξ'] = "x", ['ο'] = "o", ['π'] = "p", ['ρ'] = "r", ['σ'] = "s",
        ['ς'] = "s", ['τ'] = "t", ['υ'] = "y", ['φ'] = "f", ['χ'] = "ch", ['ψ'] = "ps",
        ['ω'] = "o",
    };

    public static string FromFileName(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var latin = Transliterate(StripMarks(name.ToLowerInvariant()));

        var slug = new StringBuilder(latin.Length);
        foreach (var c in latin)
        {
            if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                slug.Append(c);
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        var result = slug.ToString().Trim('-');
        if (result.Length > MaxLength)
        {
            result = result[..MaxLength].TrimEnd('-');
        }

        return result.Length == 0 ? Fallback : result;
    }

    private static string StripMarks(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private static string Transliterate(string value)
    {
        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == 'ο' && i + 1 < value.Length && value[i + 1] == 'υ')
            {
                builder.Append("ou");
                i++;
            }
            else if (GreekLetters.TryGetValue(value[i], out var latin))
            {
                builder.Append(latin);
            }
            else
            {
                builder.Append(value[i]);
            }
        }

        return builder.ToString();
    }
}
