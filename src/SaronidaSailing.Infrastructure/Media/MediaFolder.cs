namespace SaronidaSailing.Infrastructure.Media;

public static class MediaFolder
{
    public static void EnsureExists(MediaOptions options)
    {
        Directory.CreateDirectory(options.RootPath);
        Directory.CreateDirectory(options.OriginalsPath);
    }

    public static bool IsInside(string path, string folder)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(folder))
        {
            return false;
        }

        var relative = Path.GetRelativePath(folder, path);
        return !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }
}
