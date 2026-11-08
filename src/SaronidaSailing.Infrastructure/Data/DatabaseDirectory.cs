namespace SaronidaSailing.Infrastructure.Data;

internal static class DatabaseDirectory
{
    public static void EnsureExistsForFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)
            || filePath == ":memory:"
            || filePath.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
