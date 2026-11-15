using System.Net;
using Microsoft.Extensions.DependencyInjection;
using SaronidaSailing.Core.Interfaces;
using static SaronidaSailing.Tests.AdminTestHelpers;

namespace SaronidaSailing.Tests;

[Collection(WebHostCollection.Name)]
public class SiteSettingsTests(TempDatabaseWebApplicationFactory factory) : IClassFixture<TempDatabaseWebApplicationFactory>
{
    private const string Page = "/admin/settings";
    private const string DefaultRobots = "User-agent: *\nDisallow: /admin\n\nSitemap: https://localhost/sitemap.xml\n";

    private async Task SaveDirectAsync(string? text)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<ISiteSettingsService>().SaveRobotsTxtAsync(text, "direct@example.com", null);
        Assert.True(result.Succeeded);
    }

    private async Task<HttpClient> AdminClientAsync()
    {
        var client = NewClient(factory);
        await LoginWithTotpAsync(client, await AddTwoFactorAdminAsync(factory, $"settings-{Guid.NewGuid():N}@example.com"));
        return client;
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string handler, string? text)
    {
        var form = await client.GetAsync(Page);
        form.EnsureSuccessStatusCode();
        var data = new Dictionary<string, string> { ["__RequestVerificationToken"] = ExtractToken(await form.Content.ReadAsStringAsync()) };
        if (text is not null)
        {
            data["RobotsText"] = text;
        }

        return await client.PostAsync(handler.Length == 0 ? Page : $"{Page}?handler={handler}", new FormUrlEncodedContent(data));
    }

    [Fact]
    public async Task Robots_NothingStored_IsTheDefault()
    {
        await SaveDirectAsync(null);

        Assert.Equal(DefaultRobots, await NewClient(factory).GetStringAsync("/robots.txt"));
    }

    [Fact]
    public async Task Robots_CustomText_IsServedWithNormalisedLineEndings()
    {
        await SaveDirectAsync("User-agent: *  \r\nDisallow: /secret\r\n\r\n\r\n");
        try
        {
            var response = await NewClient(factory).GetAsync("/robots.txt");

            Assert.Equal("User-agent: *\nDisallow: /secret\n", await response.Content.ReadAsStringAsync());
            Assert.Equal("text/plain; charset=utf-8", response.Content.Headers.ContentType!.ToString());
            Assert.Equal("public, max-age=3600", response.Headers.CacheControl!.ToString());
        }
        finally
        {
            await SaveDirectAsync(null);
        }
    }

    [Fact]
    public async Task Page_Anonymous_IsRedirectedToLogin()
    {
        var response = await NewClient(factory).GetAsync(Page);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("https://localhost/admin/login?ReturnUrl=%2Fadmin%2Fsettings", Location(response));
    }

    [Fact]
    public async Task Page_PasswordOnlyUser_IsRedirectedToTwoFactorSetup()
    {
        var client = NewClient(factory);
        await AddAdminAsync(factory, "settings-nomfa@example.com");
        await LoginAsync(client, "settings-nomfa@example.com");

        var response = await client.GetAsync(Page);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/admin/two-factor/setup", Location(response));
    }

    [Fact]
    public async Task Page_ShowsTheFormTheDefaultAndWhatIsServed_NoStore()
    {
        await SaveDirectAsync(null);
        var client = await AdminClientAsync();

        var response = await client.GetAsync(Page);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<h1>Site settings</h1>", html);
        Assert.Contains("<h2>robots.txt</h2>", html);
        Assert.Contains("<label for=\"RobotsText\">robots.txt contents</label>", html);
        Assert.Contains("Leave empty to use the default below.", html);
        Assert.Contains("Served now", html);
        Assert.Contains("Sitemap: https://localhost/sitemap.xml", html);
        Assert.Contains("Open /robots.txt (opens in a new tab)", html);
        Assert.DoesNotContain("This server is a preview", html);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task Save_StoresTheText_Audits_WithoutTheText()
    {
        var client = await AdminClientAsync();
        try
        {
            var response = await PostAsync(client, "", "User-agent: *\r\nDisallow: /unique-marker-path  \r\n");

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal("/admin/settings?result=saved", Location(response));
            Assert.Equal("User-agent: *\nDisallow: /unique-marker-path\n", await NewClient(factory).GetStringAsync("/robots.txt"));
            var html = await client.GetStringAsync(Location(response));
            Assert.Contains("robots.txt saved.", html);
            Assert.Contains("role=\"status\"", html);
            var audit = ReadLogLines(factory.LogsPath, "audit");
            Assert.Contains(audit, l => l.Contains("|robots-txt-saved|") && l.Contains("characters=44"));
            Assert.DoesNotContain(audit, l => l.Contains("unique-marker-path"));
        }
        finally
        {
            await SaveDirectAsync(null);
        }
    }

    [Fact]
    public async Task Save_TooLong_ShowsAFieldError_AndStoresNothing()
    {
        await SaveDirectAsync(null);
        var client = await AdminClientAsync();

        var response = await PostAsync(client, "", new string('a', 10_001));

        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("robots.txt can have at most 10,000 characters.", html);
        Assert.Contains("role=\"alert\"", html);
        Assert.Contains("aria-invalid=\"true\"", html);
        Assert.Contains("robots-hint robots-error", html);
        Assert.Equal(DefaultRobots, await NewClient(factory).GetStringAsync("/robots.txt"));
    }

    [Fact]
    public async Task Save_AtTheLimit_IsAccepted()
    {
        var client = await AdminClientAsync();
        try
        {
            var response = await PostAsync(client, "", new string('a', 9_999));

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }
        finally
        {
            await SaveDirectAsync(null);
        }
    }

    [Fact]
    public async Task UseDefault_ClearsTheStoredText_AndAudits()
    {
        await SaveDirectAsync("User-agent: *\nDisallow: /x\n");
        var client = await AdminClientAsync();

        var response = await PostAsync(client, "UseDefault", null);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/admin/settings?result=default", Location(response));
        Assert.Equal(DefaultRobots, await NewClient(factory).GetStringAsync("/robots.txt"));
        Assert.Contains(ReadLogLines(factory.LogsPath, "audit"), l => l.Contains("|robots-txt-saved|") && l.EndsWith("default"));
        var html = await client.GetStringAsync(Location(response));
        Assert.Contains("robots.txt reset to the default.", html);
    }

    [Theory]
    [InlineData("")]
    [InlineData("UseDefault")]
    public async Task Post_WithoutAntiforgery_IsRejected(string handler)
    {
        var client = await AdminClientAsync();

        var response = await client.PostAsync(
            handler.Length == 0 ? Page : $"{Page}?handler={handler}",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["RobotsText"] = "x" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Toolbar_HasTheSettingsLink_ForTheAdminOnly()
    {
        var admin = await AdminClientAsync();

        var adminHtml = await admin.GetStringAsync("/");
        var visitorHtml = await NewClient(factory).GetStringAsync("/");

        Assert.Contains("<a class=\"cms-toolbar-link\" href=\"/admin/settings\">Settings</a>", adminHtml);
        Assert.DoesNotContain("/admin/settings", visitorHtml);
    }
}

[Collection(WebHostCollection.Name)]
public class SiteSettingsPreviewTests(PreviewWebApplicationFactory factory) : IClassFixture<PreviewWebApplicationFactory>
{
    [Fact]
    public async Task Robots_IgnoresTheStoredText_AndThePageExplainsIt()
    {
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ISiteSettingsService>().SaveRobotsTxtAsync("User-agent: *\nAllow: /\n", "p@example.com", null);
        }

        var client = NewClient(factory);
        await LoginWithTotpAsync(client, await AddTwoFactorAdminAsync(factory, "settings-preview@example.com"));

        Assert.Equal("User-agent: *\nDisallow: /\n", await NewClient(factory).GetStringAsync("/robots.txt"));
        var html = await client.GetStringAsync("/admin/settings");
        Assert.Contains("This server is a preview: robots.txt always blocks all crawlers, whatever is saved here.", html);
        var served = WebUtility.HtmlDecode(html[html.IndexOf("Served now", StringComparison.Ordinal)..]);
        Assert.Contains("<pre class=\"admin-pre\">User-agent: *\nDisallow: /\n</pre>", served.ReplaceLineEndings("\n"));
        Assert.DoesNotContain("Allow: /", served.Replace("Disallow", ""));
    }
}
