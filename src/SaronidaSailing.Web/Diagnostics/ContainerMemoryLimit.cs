namespace SaronidaSailing.Web.Diagnostics;

public static class ContainerMemoryLimit
{
    public const string CgroupV2Path = "/sys/fs/cgroup/memory.max";
    public const string CgroupV1Path = "/sys/fs/cgroup/memory/memory.limit_in_bytes";

    private const long UnlimitedSentinel = 1L << 60;

    private static readonly Lazy<long> Cached = new(() => Resolve(ReadFileOrNull));

    public static long Bytes => Cached.Value;

    public static long? Parse(string? content) =>
        long.TryParse(content?.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var bytes)
        && bytes > 0 && bytes < UnlimitedSentinel
            ? bytes
            : null;

    public static long Resolve(Func<string, string?> readFile) =>
        Parse(readFile(CgroupV2Path))
        ?? Parse(readFile(CgroupV1Path))
        ?? GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;

    private static string? ReadFileOrNull(string path)
    {
        try
        {
            return OperatingSystem.IsLinux() && File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
