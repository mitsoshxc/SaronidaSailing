namespace SaronidaSailing.Core.Services;

public static class InstagramPostCount
{
    public const int Default = 6;

    public const int Min = 1;

    public const int Max = 12;

    public static bool IsValid(string? value) =>
        value is { Length: > 0 and <= 2 }
        && value.All(char.IsAsciiDigit)
        && int.Parse(value) is >= Min and <= Max;

    public static int Parse(string? value) => IsValid(value) ? int.Parse(value!) : Default;
}
