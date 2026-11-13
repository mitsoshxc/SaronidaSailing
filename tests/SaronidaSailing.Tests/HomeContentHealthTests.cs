using System.Net;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Infrastructure.Health;

namespace SaronidaSailing.Tests;

internal static class HomeContentTestData
{
    public static async Task EnsureGreekHomeContentAsync(string connectionString)
    {
        await using var context = new ContentSeedTestDatabase(connectionString).CreateContext();
        if (context.Set<ContentItem>().Any(i => i.PageKey == "home" && i.Locale == "el"))
        {
            return;
        }

        context.Set<ContentItem>().Add(
            ContentItem.Create("home", "hero", "title", "el", null, ContentType.Text, "Τίτλος", "test@example.com"));
        await context.SaveChangesAsync();
    }
}

public class HomeContentHealthCheckTests : IAsyncLifetime
{
    private string _connectionString = "";

    public async Task InitializeAsync() => _connectionString = await PostgresTestServer.CreateDatabaseAsync();

    public Task DisposeAsync() => PostgresTestServer.DropDatabaseAsync(PostgresTestServer.DatabaseName(_connectionString));

    private async Task<HealthCheckResult> CheckAsync()
    {
        await using var context = new ContentSeedTestDatabase(_connectionString).CreateContext();
        return await new HomeContentHealthCheck(context).CheckHealthAsync(new HealthCheckContext());
    }

    private async Task AddAsync(string page, string locale)
    {
        await using var context = new ContentSeedTestDatabase(_connectionString).CreateContext();
        context.Set<ContentItem>().Add(
            ContentItem.Create(page, "hero", "title", locale, null, ContentType.Text, "x", "test@example.com"));
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task EmptyDatabase_IsUnhealthy_WithAMessage()
    {
        var result = await CheckAsync();

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("No Greek home content.", result.Description);
    }

    [Fact]
    public async Task GreekHomeContent_IsHealthy()
    {
        await AddAsync("home", "el");

        Assert.Equal(HealthStatus.Healthy, (await CheckAsync()).Status);
    }

    [Fact]
    public async Task EnglishHomeOnly_IsUnhealthy()
    {
        await AddAsync("home", "en");

        Assert.Equal(HealthStatus.Unhealthy, (await CheckAsync()).Status);
    }

    [Fact]
    public async Task GreekContentOfAnotherPage_IsUnhealthy()
    {
        await AddAsync("shared", "el");

        Assert.Equal(HealthStatus.Unhealthy, (await CheckAsync()).Status);
    }
}

[Collection(WebHostCollection.Name)]
public class HomeContentHealthWebHostTests(TempDatabaseWebApplicationFactory factory)
    : IClassFixture<TempDatabaseWebApplicationFactory>
{
    [Fact]
    public async Task Get_Health_WithoutHomeContent_ReturnsUnhealthy()
    {
        var response = await factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync());
    }
}
