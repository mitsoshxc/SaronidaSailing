namespace SaronidaSailing.Core.Services;

public static class MediaDimensions
{
    public static int ScaledHeight(int sourceWidth, int sourceHeight, int targetWidth) =>
        Math.Max(1, (int)Math.Round(sourceHeight * (double)targetWidth / sourceWidth));
}
