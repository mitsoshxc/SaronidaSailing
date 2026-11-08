namespace SaronidaSailing.Infrastructure.Media;

public static class MediaFolder
{
    public static void EnsureExists(string rootPath) => Directory.CreateDirectory(rootPath);
}
