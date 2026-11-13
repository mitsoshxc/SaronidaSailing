namespace SaronidaSailing.Core.Services;

public static class ContactMap
{
    public const string EmbedUrlKey = "contact.map.embed-url";

    public const string EmbedUrlPrefix = "https://www.google.com/maps/embed";

    public static bool IsValidEmbedUrl(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return true;
        }

        return value.StartsWith(EmbedUrlPrefix, StringComparison.Ordinal)
            && (value.Length == EmbedUrlPrefix.Length || value[EmbedUrlPrefix.Length] is '?' or '/')
            && !value.Any(char.IsWhiteSpace);
    }
}
