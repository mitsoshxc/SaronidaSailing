using System.Net;
using Microsoft.AspNetCore.Hosting;
using SaronidaSailing.Web.Security;
using static SaronidaSailing.Tests.AdminTestHelpers;

namespace SaronidaSailing.Tests;

[Collection(WebHostCollection.Name)]
public class SecurityHeadersTests(TempDatabaseWebApplicationFactory factory) : IClassFixture<TempDatabaseWebApplicationFactory>, IAsyncLifetime
{
    private const string ExpectedCsp =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self'; font-src 'self'; connect-src 'self'; "
        + "frame-src https://www.google.com; frame-ancestors 'self'; base-uri 'self'; "
        + "form-action 'self' https://www.instagram.com; object-src 'none'";

    private const string ExpectedAdminCsp =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self'; "
        + "frame-src https://www.google.com; frame-ancestors 'self'; base-uri 'self'; "
        + "form-action 'self' https://www.instagram.com; object-src 'none'";

    public Task InitializeAsync() => HomeContentTestData.EnsureGreekHomeContentAsync(factory.ContentConnectionString);

    public Task DisposeAsync() => Task.CompletedTask;

    private static void AssertSecurityHeaders(HttpResponseMessage response, string csp = ExpectedCsp)
    {
        Assert.Equal(csp, Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("strict-origin-when-cross-origin", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/en/")]
    [InlineData("/this-page-does-not-exist")]
    [InlineData("/en/this-page-does-not-exist")]
    [InlineData("/robots.txt")]
    [InlineData("/sitemap.xml")]
    [InlineData("/css/site.css")]
    public async Task PublicResponses_CarrySecurityHeaders(string path)
    {
        var response = await NewClient(factory).GetAsync(path);

        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task NotFoundPage_And_StaticAsset_HaveExpectedStatusAndHeaders()
    {
        var notFound = await NewClient(factory).GetAsync("/this-page-does-not-exist");
        var asset = await NewClient(factory).GetAsync("/css/site.css");

        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
        AssertSecurityHeaders(notFound);
        AssertSecurityHeaders(asset);
    }

    [Fact]
    public async Task Health_Direct_CarriesSecurityHeaders()
    {
        var response = await factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task Health_ThroughTheProxy_Is404WithSecurityHeaders()
    {
        var client = NewClient(factory);
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.9");

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task LoginRateLimitPage_CarriesSecurityHeaders()
    {
        using var client = NewClientFromIp(factory, "10.40.0.1");
        HttpResponseMessage response = null!;
        for (var i = 0; i <= SaronidaSailing.Web.Identity.AdminLoginRateLimit.PermitLimit; i++)
        {
            response = await LoginAsync(client, "headers-limited@example.com", "wrong-" + i);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task AdminApi_Unauthorized401_CarriesSecurityHeaders()
    {
        var response = await NewClient(factory).PostAsync("/admin/api/content", new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task AdminRedirectToLogin_CarriesSecurityHeaders()
    {
        var response = await NewClient(factory).GetAsync("/admin/jobs");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task HangfireDashboard_AsAdmin_CarriesSecurityHeaders()
    {
        var client = NewClient(factory);
        var admin = await AddTwoFactorAdminAsync(factory, "headers-jobs@example.com");
        await LoginWithTotpAsync(client, admin);

        var response = await client.GetAsync("/admin/jobs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertSecurityHeaders(response, ExpectedAdminCsp);
    }

    [Fact]
    public async Task AdminLoginPage_AsVisitor_GetsThePublicPolicy()
    {
        var response = await NewClient(factory).GetAsync("/admin/login");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task HomePage_AsTwoFactorAdmin_GetsTheAdminPolicy()
    {
        var client = NewClient(factory);
        var admin = await AddTwoFactorAdminAsync(factory, "headers-home@example.com");
        await LoginWithTotpAsync(client, admin);

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertSecurityHeaders(response, ExpectedAdminCsp);
    }

    [Fact]
    public async Task TwoFactorSetupPage_AsPasswordOnlyUser_GetsThePublicPolicy()
    {
        var client = NewClient(factory);
        await AddAdminAsync(factory, "headers-setup@example.com");
        var login = await LoginAsync(client, "headers-setup@example.com");
        Assert.Equal("/admin/two-factor/setup", Location(login));

        var response = await client.GetAsync("/admin/two-factor/setup");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertSecurityHeaders(response);
    }

    [Fact]
    public void AdminPolicy_DiffersFromPublic_OnlyInStyleSrcAndImgSrc()
    {
        const string url = "https://www.instagram.com/oauth/authorize";

        Assert.Equal(ExpectedCsp, SecurityHeaders.BuildContentSecurityPolicy(url, admin: false));
        Assert.Equal(ExpectedAdminCsp, SecurityHeaders.BuildContentSecurityPolicy(url, admin: true));
        Assert.Equal(
            ExpectedAdminCsp,
            ExpectedCsp.Replace("style-src 'self';", "style-src 'self' 'unsafe-inline';").Replace("img-src 'self';", "img-src 'self' data:;"));
    }

    [Theory]
    [InlineData("https://www.instagram.com/oauth/authorize", "https://www.instagram.com")]
    [InlineData("https://auth.example.org:8443/oauth/authorize?x=1", "https://auth.example.org:8443")]
    public void FormAction_FollowsTheInstagramAuthorizeUrl(string authorizeUrl, string expectedOrigin)
    {
        var csp = SecurityHeaders.BuildContentSecurityPolicy(authorizeUrl);

        Assert.Contains($"form-action 'self' {expectedOrigin};", csp);
    }

    [Fact]
    public async Task FormAction_InTheRunningApp_FollowsTheConfiguredAuthorizeUrl()
    {
        using var custom = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Instagram:AuthorizeUrl", "https://auth.example.org/oauth/authorize"));

        var response = await NewClient(custom).GetAsync("/robots.txt");

        AssertSecurityHeaders(response, ExpectedCsp.Replace("https://www.instagram.com", "https://auth.example.org"));
    }
}
