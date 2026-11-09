namespace SaronidaSailing.Core.Services;

public static class ContentLocales
{
    public const string Greek = "el";

    public const string English = "en";

    public const string Default = Greek;

    public static IReadOnlyList<string> Supported { get; } = [Greek, English];

    public static bool IsSupported(string? locale) => locale is not null && Supported.Contains(locale);
}
