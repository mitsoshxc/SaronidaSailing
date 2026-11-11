using System.Net;
using static SaronidaSailing.Tests.AdminTestHelpers;

namespace SaronidaSailing.Tests;

[Collection(WebHostCollection.Name)]
public class AdminRootRedirectTests(TempDatabaseWebApplicationFactory factory) : IClassFixture<TempDatabaseWebApplicationFactory>
{
    [Theory]
    [InlineData("/admin")]
    [InlineData("/admin/")]
    public async Task AdminRoot_RedirectsToLogin_AndIsNotCached(string path)
    {
        var client = NewClient(factory);

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/admin/login", Location(response));
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task AdminRoot_HeadRequest_AlsoRedirectsToLogin()
    {
        var client = NewClient(factory);
        using var request = new HttpRequestMessage(HttpMethod.Head, "/admin");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/admin/login", Location(response));
    }

    [Fact]
    public async Task SignedInAdmin_FollowingTheRedirects_EndsOnTheSite()
    {
        var client = NewClient(factory);
        await LoginWithTotpAsync(client, await AddTwoFactorAdminAsync(factory, "root-redirect@example.com"));

        var root = await client.GetAsync("/admin");
        Assert.Equal(HttpStatusCode.Redirect, root.StatusCode);
        Assert.Equal("/admin/login", Location(root));

        var login = await client.GetAsync("/admin/login");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/", Location(login));

        var home = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
    }

    [Fact]
    public async Task EnglishAdminRoot_IsNotRouted()
    {
        var client = NewClient(factory);

        var response = await client.GetAsync("/en/admin");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task OtherAdminRoutes_AreUnchanged()
    {
        var client = NewClient(factory);

        var login = await client.GetAsync("/admin/login");
        var unknown = await client.GetAsync("/admin/xyz");

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }
}
