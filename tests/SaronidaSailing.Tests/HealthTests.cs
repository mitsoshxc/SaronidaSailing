using Microsoft.AspNetCore.Hosting;
using System.Net;

namespace SaronidaSailing.Tests;

[Collection(WebHostCollection.Name)]
public class HealthTests(TempDatabaseWebApplicationFactory factory) : IClassFixture<TempDatabaseWebApplicationFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => HomeContentTestData.EnsureGreekHomeContentAsync(factory.ContentConnectionString);

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Get_Health_WhenEverythingWorks_ReturnsHealthy()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/HEALTH")]
    [InlineData("/health/")]
    public async Task Get_Health_ThroughTheProxy_Returns404(string path)
    {
        var client = AdminTestHelpers.NewClient(factory);
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.9");

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Get_Health_WithForwardedHeaderAndNoRemoteIp_Returns404()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.9");

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_Health_SendsNoCacheHeaders()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        var cacheControl = response.Headers.CacheControl;
        Assert.NotNull(cacheControl);
        Assert.True(cacheControl.NoStore || cacheControl.NoCache);
    }

    [Fact]
    public async Task Get_Health_WhenMediaFolderIsMissing_ReturnsUnhealthy()
    {
        var missing = Path.Combine(factory.MediaPath, "missing");
        using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Media:RootPath", missing);
        });
        var client = host.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync());
        Assert.False(Directory.Exists(missing));
    }

    [Fact]
    public async Task Get_Health_WhenOriginalsFolderIsMissing_ReturnsUnhealthy()
    {
        var missing = Path.Combine(factory.OriginalsPath, "missing");
        using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Media:OriginalsPath", missing);
        });
        var client = host.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync());
        Assert.False(Directory.Exists(missing));
    }

    [Fact]
    public async Task Get_Health_WhenHomePageDoesNotAnswerOk_ReturnsUnhealthy()
    {
        using var host = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Health:HomePageUrl", "http://localhost/does-not-exist"));
        var client = host.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync());
    }
}
