using System.Net;
using System.Text.RegularExpressions;
using static SaronidaSailing.Tests.AdminTestHelpers;

namespace SaronidaSailing.Tests;

[Collection(WebHostCollection.Name)]
public class HangfireDashboardTests(TempDatabaseWebApplicationFactory factory) : IClassFixture<TempDatabaseWebApplicationFactory>
{
    [Fact]
    public async Task Anonymous_IsRedirectedToLogin_WithReturnUrl()
    {
        var client = NewClient(factory);

        var response = await client.GetAsync("/admin/jobs");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("https://localhost/admin/login?ReturnUrl=%2Fadmin%2Fjobs", Location(response));
    }

    [Fact]
    public async Task Anonymous_SubPathAndApiPost_AreNeverServed()
    {
        var client = NewClient(factory);

        var sub = await client.GetAsync("/admin/jobs/recurring");
        var api = await client.PostAsync("/admin/jobs/stats", new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.Contains(sub.StatusCode, new[] { HttpStatusCode.Redirect, HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden });
        Assert.Contains(api.StatusCode, new[] { HttpStatusCode.Redirect, HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden });
    }

    [Fact]
    public async Task PasswordOnlyUser_IsRedirectedToTwoFactorSetup()
    {
        var client = NewClient(factory);
        await AddAdminAsync(factory, "jobs-nomfa@example.com");
        await LoginAsync(client, "jobs-nomfa@example.com");

        var response = await client.GetAsync("/admin/jobs");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/admin/two-factor/setup", Location(response));
    }

    [Fact]
    public async Task TwoFactorAdmin_SeesDashboard()
    {
        var client = NewClient(factory);
        var admin = await AddTwoFactorAdminAsync(factory, "jobs-admin@example.com");
        await LoginWithTotpAsync(client, admin);

        var response = await client.GetAsync("/admin/jobs");

        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Saronida Sailing jobs", html);
    }

    [Fact]
    public async Task DashboardPost_WithoutAntiforgeryToken_IsRejected()
    {
        var client = NewClient(factory);
        var admin = await AddTwoFactorAdminAsync(factory, "jobs-csrf-no@example.com");
        await LoginWithTotpAsync(client, admin);

        var response = await client.PostAsync("/admin/jobs/stats", new FormUrlEncodedContent([new("metrics[]", "enqueued:count")]));

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.Forbidden });
    }

    [Fact]
    public async Task DashboardPost_WithAntiforgeryTokenFromPage_IsServed()
    {
        var client = NewClient(factory);
        var admin = await AddTwoFactorAdminAsync(factory, "jobs-csrf-yes@example.com");
        await LoginWithTotpAsync(client, admin);
        var html = await (await client.GetAsync("/admin/jobs")).Content.ReadAsStringAsync();
        var header = Regex.Match(html, "<meta name=\"csrf-header\" content=\"([^\"]+)\"").Groups[1].Value;
        var token = WebUtility.HtmlDecode(Regex.Match(html, "<meta name=\"csrf-token\" content=\"([^\"]+)\"").Groups[1].Value);
        Assert.False(string.IsNullOrEmpty(header));
        Assert.False(string.IsNullOrEmpty(token));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/admin/jobs/stats")
        {
            Content = new FormUrlEncodedContent([new("metrics[]", "enqueued:count")]),
        };
        request.Headers.Add(header, token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithJobsReturnUrl_EndsAtDashboard()
    {
        var client = NewClient(factory);
        var admin = await AddTwoFactorAdminAsync(factory, "jobs-return@example.com");

        var password = await LoginAsync(client, admin.Email, url: "/admin/login?returnUrl=%2Fadmin%2Fjobs");
        Assert.Equal("/admin/login/code?returnUrl=%2Fadmin%2Fjobs", Location(password));
        var code = await PostFormAsync(client, "/admin/login/code?returnUrl=%2Fadmin%2Fjobs", ("Code", Totp.Current(admin.Key)));

        Assert.Equal(HttpStatusCode.Redirect, code.StatusCode);
        Assert.Equal("/admin/jobs", Location(code));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/admin/jobs")).StatusCode);
    }
}
