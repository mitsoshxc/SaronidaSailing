namespace SaronidaSailing.Core.Services;

public static class MediaLimits
{
    public const int MaxUploadBytes = 25 * 1024 * 1024;

    public const int MaxPixels = 50_000_000;

    public const int WebpQuality = 80;

    public static readonly IReadOnlyList<int> Widths = [480, 960, 1600, 2400];
}
