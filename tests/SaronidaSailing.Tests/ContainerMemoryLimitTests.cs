using SaronidaSailing.Web.Diagnostics;

namespace SaronidaSailing.Tests;

public class ContainerMemoryLimitTests
{
    private static Func<string, string?> Files(string? v2, string? v1) =>
        path => path switch
        {
            ContainerMemoryLimit.CgroupV2Path => v2,
            ContainerMemoryLimit.CgroupV1Path => v1,
            _ => throw new InvalidOperationException(path),
        };

    [Theory]
    [InlineData("max", null)]
    [InlineData("max\n", null)]
    [InlineData("536870912", 536870912L)]
    [InlineData("536870912\n", 536870912L)]
    [InlineData("9223372036854771712", null)]
    [InlineData("0", null)]
    [InlineData("-5", null)]
    [InlineData("garbage", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Parse_ReturnsBytesOrNullForNoLimit(string? content, long? expected) =>
        Assert.Equal(expected, ContainerMemoryLimit.Parse(content));

    [Fact]
    public void Resolve_PrefersCgroupV2()
    {
        var bytes = ContainerMemoryLimit.Resolve(Files("536870912\n", "268435456"));

        Assert.Equal(512L * 1024 * 1024, bytes);
    }

    [Fact]
    public void Resolve_V2Max_FallsBackToV1()
    {
        var bytes = ContainerMemoryLimit.Resolve(Files("max\n", "268435456\n"));

        Assert.Equal(256L * 1024 * 1024, bytes);
    }

    [Fact]
    public void Resolve_OnlyV1_UsesV1()
    {
        var bytes = ContainerMemoryLimit.Resolve(Files(null, "1073741824"));

        Assert.Equal(1024L * 1024 * 1024, bytes);
    }

    [Fact]
    public void Resolve_NoLimitAnywhere_FallsBackToPhysicalMemory()
    {
        var bytes = ContainerMemoryLimit.Resolve(Files("max", "9223372036854771712"));

        Assert.Equal(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes, bytes);
    }

    [Fact]
    public void Resolve_UnreadableFiles_FallsBackToPhysicalMemory()
    {
        var bytes = ContainerMemoryLimit.Resolve(Files(null, null));

        Assert.Equal(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes, bytes);
    }
}
