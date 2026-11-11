using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace SaronidaSailing.Tests;

public sealed class MissingOriginalsWebApplicationFactory : TempDatabaseWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Media:OriginalsPath", "");
    }
}

[Collection(WebHostCollection.Name)]
public class MediaServingTests(TempDatabaseWebApplicationFactory factory) : IClassFixture<TempDatabaseWebApplicationFactory>
{
    [Fact]
    public async Task Development_ServesWebpWithImmutableCacheHeader()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        factory.CreateClient();
        await File.WriteAllBytesAsync(Path.Combine(factory.MediaPath, "x.webp"), bytes);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/media/x.webp");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/webp", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("public, max-age=31536000, immutable", response.Headers.CacheControl?.ToString());
        Assert.Equal(bytes, await response.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [InlineData("x.txt")]
    [InlineData("x.jpg")]
    public async Task Development_DoesNotServeOtherFileTypes(string name)
    {
        factory.CreateClient();
        await File.WriteAllTextAsync(Path.Combine(factory.MediaPath, name), "data");
        var client = factory.CreateClient();

        var response = await client.GetAsync("/media/" + name);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/media/..%2Fsecret.webp")]
    [InlineData("/media/%2e%2e/secret.webp")]
    [InlineData("/media/..%5Csecret.webp")]
    [InlineData("/media/%2e%2e%2foriginals%2Fhidden.webp")]
    public async Task Development_PathTraversalReturnsNotFound(string path)
    {
        factory.CreateClient();
        var root = Directory.GetParent(factory.MediaPath)!.FullName;
        await File.WriteAllTextAsync(Path.Combine(root, "secret.webp"), "secret");
        await File.WriteAllTextAsync(Path.Combine(factory.OriginalsPath, "hidden.webp"), "secret");
        var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Development_NeverServesTheOriginalsFolder()
    {
        factory.CreateClient();
        await File.WriteAllTextAsync(Path.Combine(factory.OriginalsPath, "abc.webp"), "secret");
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/media/abc.webp")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/originals/abc.webp")).StatusCode);
    }

    [Fact]
    public async Task Production_DoesNotServeMedia()
    {
        factory.CreateClient();
        await File.WriteAllBytesAsync(Path.Combine(factory.MediaPath, "p.webp"), [1, 2, 3]);
        using var host = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        var client = host.CreateClient();

        var response = await client.GetAsync("/media/p.webp");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

[Collection(WebHostCollection.Name)]
public class MediaOptionsStartupTests
{
    [Fact]
    public void Startup_WithoutOriginalsPath_FailsAndNamesTheKey()
    {
        using var factory = new MissingOriginalsWebApplicationFactory();

        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(Messages(ex), m => m.Contains("Media:OriginalsPath is not configured.", StringComparison.Ordinal));
    }

    [Fact]
    public void Startup_WithOriginalsInsideMediaFolder_Fails()
    {
        using var factory = new TempDatabaseWebApplicationFactory();
        using var host = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Media:OriginalsPath", Path.Combine(factory.MediaPath, "originals")));

        var ex = Assert.ThrowsAny<Exception>(() => host.CreateClient());

        Assert.Contains(Messages(ex), m => m.Contains("must not be inside Media:RootPath", StringComparison.Ordinal));
    }

    private static List<string> Messages(Exception ex)
    {
        var messages = new List<string>();
        for (var current = ex; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return messages;
    }
}
