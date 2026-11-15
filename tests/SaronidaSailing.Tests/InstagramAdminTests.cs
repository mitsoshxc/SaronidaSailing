using System.Net;
using System.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Core.Services;
using SaronidaSailing.Infrastructure.Data;
using SaronidaSailing.Infrastructure.Jobs;
using static SaronidaSailing.Tests.AdminTestHelpers;

namespace SaronidaSailing.Tests;

public sealed class InstagramWebApplicationFactory : TempDatabaseWebApplicationFactory
{
    public FakeTimeProvider Time { get; } = new(DateTimeOffset.UtcNow);

    public FakeInstagramClient Instagram { get; } = new();

    public FakeInstagramJobQueue JobQueue { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
            services.RemoveAll<IInstagramApiClient>();
            services.AddSingleton<IInstagramApiClient>(Instagram);
            services.RemoveAll<IInstagramJobQueue>();
            services.AddSingleton<IInstagramJobQueue>(JobQueue);
        });
    }
}

public sealed class FakeInstagramClient : IInstagramApiClient
{
    public const string LongToken = "LONG-LIVED-TOKEN-xyz";

    public InstagramApiException? Failure { get; set; }

    public string BuildAuthorizeUrl(InstagramAppCredentials credentials, string redirectUri, string state) =>
        $"https://www.instagram.com/oauth/authorize?client_id={credentials.AppId}&redirect_uri={Uri.EscapeDataString(redirectUri)}&state={Uri.EscapeDataString(state)}";

    public Task<InstagramShortLivedToken> ExchangeCodeAsync(InstagramAppCredentials credentials, string code, string redirectUri, CancellationToken ct = default) =>
        Failure is null
            ? Task.FromResult(new InstagramShortLivedToken("SHORT", "17841400000000001"))
            : Task.FromException<InstagramShortLivedToken>(Failure);

    public Task<InstagramLongLivedToken> ExchangeForLongLivedAsync(InstagramAppCredentials credentials, string shortLivedToken, CancellationToken ct = default) =>
        Task.FromResult(new InstagramLongLivedToken(LongToken, DateTime.UtcNow.AddDays(60)));

    public Task<InstagramLongLivedToken> RefreshAsync(string longLivedToken, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<InstagramProfile> GetProfileAsync(string token, CancellationToken ct = default) =>
        Task.FromResult(new InstagramProfile("17841400000000001", "saronida"));

    public Task<IReadOnlyList<InstagramMediaItem>> GetRecentMediaAsync(string token, int limit, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<Stream> DownloadImageAsync(string url, CancellationToken ct = default) => throw new NotSupportedException();
}

[Collection(WebHostCollection.Name)]
public class InstagramAdminTests(InstagramWebApplicationFactory factory) : IClassFixture<InstagramWebApplicationFactory>
{
    private const string Page = "/admin/instagram";
    private const string CookieName = "saronida.ig-oauth";
    private const string SecretValue = "SUPER-SECRET-VALUE-0123456789";

    [Fact]
    public async Task Page_Anonymous_IsRedirectedToLogin()
    {
        var response = await NewClient(factory).GetAsync(Page);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("https://localhost/admin/login?ReturnUrl=%2Fadmin%2Finstagram", Location(response));
    }

    [Fact]
    public async Task Page_PasswordOnlyUser_IsRedirectedToTwoFactorSetup()
    {
        var client = NewClient(factory);
        await AddAdminAsync(factory, "ig-nomfa@example.com");
        await LoginAsync(client, "ig-nomfa@example.com");

        var response = await client.GetAsync(Page);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/admin/two-factor/setup", Location(response));
    }

    [Fact]
    public async Task Page_NotConnected_ShowsConnectButton_AndNoStore()
    {
        await ResetAsync();
        var client = await LoggedInClientAsync("ig-page@example.com");

        var response = await client.GetAsync(Page);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<h1>Instagram</h1>", html);
        Assert.Contains("Not connected.", html);
        Assert.Contains("Connect Instagram", html);
        Assert.DoesNotContain("Disconnect", html);
        Assert.DoesNotContain("Fetch now", html);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task Connect_RedirectsToTheAuthorizeUrl_AndSetsTheCorrelationCookie()
    {
        var client = await LoggedInClientAsync("ig-connect@example.com");

        var response = await PostHandlerAsync(client, "Connect");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var url = new Uri(Location(response));
        Assert.Equal("www.instagram.com", url.Host);
        var query = HttpUtility.ParseQueryString(url.Query);
        Assert.Equal("https://localhost/admin/instagram/callback", query["redirect_uri"]);
        Assert.False(string.IsNullOrEmpty(query["state"]));
        var cookie = SetCookie(response, CookieName);
        Assert.NotNull(cookie);
        var lower = cookie.ToLowerInvariant();
        Assert.Contains("samesite=lax", lower);
        Assert.Contains("httponly", lower);
        Assert.Contains("secure", lower);
        Assert.Contains("path=/admin/instagram", lower);
        Assert.Contains("max-age=600", lower);
    }

    [Fact]
    public async Task NotConfigured_DisablesConnect_AndPostRedirectsWithNotConfigured()
    {
        var client = await LoggedInClientAsync("ig-notconfigured@example.com");
        await RemoveAppAsync();

        var html = await (await client.GetAsync(Page)).Content.ReadAsStringAsync();
        var post = await PostHandlerAsync(client, "Connect");
        var banner = await (await client.GetAsync(Location(post))).Content.ReadAsStringAsync();

        const string text = "Instagram is not set up yet: enter the Meta App ID and App secret under <a href=\"#meta-app\">Meta app</a>.";
        Assert.Contains(text, html);
        Assert.Contains("disabled", html);
        Assert.Contains("aria-describedby=\"instagram-not-set-up\"", html);
        Assert.Equal("/admin/instagram?result=notconfigured", Location(post));
        Assert.Contains("role=\"alert\"", banner);
        Assert.Contains($"<p class=\"admin-error\" role=\"alert\">{text}</p>", banner);
    }

    [Fact]
    public async Task Connect_AfterSavingTheApp_RedirectsToTheAuthorizeUrlWithTheSavedAppId()
    {
        var client = await LoggedInClientAsync("ig-connect-saved@example.com");
        await RemoveAppAsync();
        var save = await PostFormAsync(client, "SaveApp", new() { ["AppId"] = " 987654321 ", ["AppSecret"] = SecretValue });
        Assert.Equal("/admin/instagram?result=app-saved", Location(save));

        var response = await PostHandlerAsync(client, "Connect");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var query = HttpUtility.ParseQueryString(new Uri(Location(response)).Query);
        Assert.Equal("987654321", query["client_id"]);
    }

    [Fact]
    public async Task SaveApp_WithoutAntiforgery_IsRejected()
    {
        var client = await LoggedInClientAsync("ig-app-csrf@example.com");

        var response = await client.PostAsync(Page + "?handler=SaveApp", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["AppId"] = "12345",
            ["AppSecret"] = SecretValue,
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RemoveApp_WithoutAntiforgery_IsRejected()
    {
        var client = await LoggedInClientAsync("ig-remove-csrf@example.com");

        var response = await client.PostAsync(Page + "?handler=RemoveApp", new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("SaveApp")]
    [InlineData("RemoveApp")]
    public async Task AppHandlers_Anonymous_AreRedirectedToLogin(string handler)
    {
        var response = await NewClient(factory).PostAsync(Page + $"?handler={handler}", new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/admin/login", Location(response));
    }

    [Fact]
    public async Task Page_NeverRendersTheSecret_AndShowsTheSavedHint()
    {
        var client = await LoggedInClientAsync("ig-secret-render@example.com");
        await RemoveAppAsync();
        var empty = await client.GetStringAsync(Page);
        await PostFormAsync(client, "SaveApp", new() { ["AppId"] = "70000005", ["AppSecret"] = SecretValue });

        var saved = await client.GetStringAsync(Page);
        var banner = await client.GetStringAsync("/admin/instagram?result=app-saved");

        Assert.DoesNotContain(SecretValue, saved);
        Assert.DoesNotContain(SecretValue, banner);
        Assert.Contains("<h2 id=\"meta-app\">Meta app</h2>", empty);
        Assert.Contains(">App secret</label>", empty);
        Assert.DoesNotContain("Remove app settings", empty);
        Assert.Contains("value=\"70000005\"", saved);
        Assert.Contains("Replace App secret (optional)", saved);
        Assert.Contains("Saved. Leave empty to keep it.", saved);
        Assert.Contains("type=\"password\"", saved);
        Assert.Contains("autocomplete=\"new-password\"", saved);
        Assert.Contains("Remove app settings", saved);
        Assert.Contains("https://localhost/admin/instagram/callback", saved);
        Assert.Contains("Meta app settings saved.", banner);
        Assert.Contains("role=\"status\"", banner);
    }

    [Fact]
    public async Task SaveApp_WithAnInvalidAppId_ShowsTheErrorOnTheFieldAndSavesNothing_WithoutEchoingTheSecret()
    {
        var client = await LoggedInClientAsync("ig-app-invalid@example.com");
        await RemoveAppAsync();

        var response = await PostFormAsync(client, "SaveApp", new() { ["AppId"] = "not-a-number", ["AppSecret"] = SecretValue });

        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("The App ID is the number shown in the Meta app dashboard (App settings &#x2192; Basic).", html);
        Assert.Contains("id=\"meta-app-error\"", html);
        Assert.Contains("aria-describedby=\"meta-app-id-help meta-app-error\"", html);
        Assert.Contains("value=\"not-a-number\"", html);
        Assert.DoesNotContain(SecretValue, html);
        Assert.False((await AppStatusAsync()).IsConfigured);
    }

    [Fact]
    public async Task SaveApp_ChangingTheAppIdWhileConnected_NeedsConfirmation_ThenDisconnects()
    {
        await RemoveAppAsync();
        var admin = await AdminWithClientAsync("ig-app-change@example.com");
        await PostFormAsync(admin.Client, "SaveApp", new() { ["AppId"] = "70000001", ["AppSecret"] = SecretValue });
        var (state, nonce) = await StartAsync(admin.Client);
        await CallbackAsync(NewClient(factory), $"code=C&state={Uri.EscapeDataString(state)}", nonce);

        var rejected = await PostFormAsync(admin.Client, "SaveApp", new() { ["AppId"] = "70000002" });
        var rejectedHtml = await rejected.Content.ReadAsStringAsync();
        var afterRejected = await AppStatusAsync();
        var secretOnly = await PostFormAsync(admin.Client, "SaveApp", new() { ["AppId"] = "70000001", ["AppSecret"] = SecretValue + "2" });
        var stillConnected = (await ConnectionStatusAsync()).State;
        var confirmed = await PostFormAsync(admin.Client, "SaveApp", new() { ["AppId"] = "70000002", ["ConfirmDisconnect"] = "true" });

        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        Assert.Contains("Changing the App ID removes the Instagram connection. Confirm to continue.", rejectedHtml);
        Assert.Contains("Save and disconnect", rejectedHtml);
        Assert.Equal("70000001", afterRejected.AppId);
        Assert.Equal("/admin/instagram?result=app-saved", Location(secretOnly));
        Assert.Equal(InstagramConnectionState.Connected, stillConnected);
        Assert.Equal("/admin/instagram?result=app-saved-disconnected", Location(confirmed));
        Assert.Equal(InstagramConnectionState.NotConnected, (await ConnectionStatusAsync()).State);
        Assert.Equal("70000002", (await AppStatusAsync()).AppId);
        var banner = await admin.Client.GetStringAsync(Location(confirmed));
        Assert.Contains("Meta app settings saved. Instagram was disconnected; connect again.", banner);
        var audit = ReadLogLines(factory.LogsPath, "audit");
        Assert.Contains(audit, l => l.Contains($"|instagram-app-saved|user={admin.Email}|") && l.Contains("app-id-changed=true secret-replaced=false disconnected=true"));
        Assert.DoesNotContain(audit, l => l.Contains(SecretValue) || l.Contains("70000001") || l.Contains("70000002"));
    }

    [Fact]
    public async Task RemoveApp_ClearsTheSettingsAndTheConnection_AndAudits()
    {
        await RemoveAppAsync();
        var admin = await AdminWithClientAsync("ig-app-remove@example.com");
        await PostFormAsync(admin.Client, "SaveApp", new() { ["AppId"] = "33333", ["AppSecret"] = SecretValue });
        var (state, nonce) = await StartAsync(admin.Client);
        await CallbackAsync(NewClient(factory), $"code=C&state={Uri.EscapeDataString(state)}", nonce);

        var response = await PostHandlerAsync(admin.Client, "RemoveApp");

        Assert.Equal("/admin/instagram?result=app-removed", Location(response));
        var status = await AppStatusAsync();
        Assert.Null(status.AppId);
        Assert.False(status.SecretSaved);
        Assert.Equal(InstagramConnectionState.NotConnected, (await ConnectionStatusAsync()).State);
        Assert.Contains("Meta app settings removed.", await admin.Client.GetStringAsync(Location(response)));
        Assert.Contains(
            ReadLogLines(factory.LogsPath, "audit"),
            l => l.Contains($"|instagram-app-removed|user={admin.Email}|") && l.Contains("disconnected=true"));
    }

    [Fact]
    public async Task Callback_WhenTheAppSettingsWereRemovedMeanwhile_RedirectsWithNotConfigured()
    {
        await ResetAsync();
        var admin = await AdminWithClientAsync("ig-callback-removed@example.com");
        var (state, nonce) = await StartAsync(admin.Client);
        await RemoveAppAsync();

        var response = await CallbackAsync(NewClient(factory), $"code=C&state={Uri.EscapeDataString(state)}", nonce);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("url=/admin/instagram?result=notconfigured", await response.Content.ReadAsStringAsync());
        await AssertNotConnectedAsync();
    }

    [Fact]
    public async Task Callback_WithoutTheAuthCookie_ConnectsAndRedirectsByMetaRefresh()
    {
        await ResetAsync();
        var admin = await AdminWithClientAsync("ig-callback@example.com");
        var (state, nonce) = await StartAsync(admin.Client);
        var anonymous = NewClient(factory);

        var response = await CallbackAsync(anonymous, $"code=SECRET-CODE-1&state={Uri.EscapeDataString(state)}", nonce);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<html lang=\"en\">", html);
        Assert.Contains("<title>Instagram</title>", html);
        Assert.Contains("<meta name=\"robots\" content=\"noindex\"", html);
        Assert.Contains("<meta http-equiv=\"refresh\" content=\"0;url=/admin/instagram?result=connected\"", html);
        Assert.Contains("href=\"/admin/instagram?result=connected\">Continue</a>", html);
        Assert.DoesNotContain("SECRET-CODE-1", html);
        Assert.DoesNotContain(state, html);
        Assert.DoesNotContain("admin-toolbar", html);
        Assert.Contains("expires=", SetCookie(response, CookieName)!.ToLowerInvariant());

        await using var scope = factory.Services.CreateAsyncScope();
        var status = await scope.ServiceProvider.GetRequiredService<IInstagramConnectionService>().GetStatusAsync();
        Assert.Equal(InstagramConnectionState.Connected, status.State);
        Assert.Equal("saronida", status.Username);
        Assert.Equal(admin.Email, status.ConnectedBy);
        Assert.Equal(FakeInstagramClient.LongToken, await scope.ServiceProvider.GetRequiredService<IInstagramConnectionService>().GetAccessTokenAsync());
        var audit = ReadLogLines(factory.LogsPath, "audit");
        Assert.Contains(audit, l => l.Contains($"|instagram-connected|user={admin.Email}|") && l.Contains("username=@saronida"));
        Assert.DoesNotContain(audit, l => l.Contains(FakeInstagramClient.LongToken) || l.Contains("SECRET-CODE-1"));

        var connectedPage = await (await admin.Client.GetAsync("/admin/instagram?result=connected")).Content.ReadAsStringAsync();
        Assert.Contains("Instagram connected.", connectedPage);
        Assert.Contains("Connected as @saronida.", connectedPage);
        Assert.Contains("Reconnect", connectedPage);
        Assert.Contains("Disconnect Instagram? Visitors keep seeing the last posts.", connectedPage);
    }

    [Fact]
    public async Task Callback_WhenInstagramRejectsTheCode_ReportsFailed_AndStoresNothing()
    {
        await ResetAsync();
        var admin = await AdminWithClientAsync("ig-rejected@example.com");
        var (state, nonce) = await StartAsync(admin.Client);
        factory.Instagram.Failure = new InstagramApiException(InstagramApiErrorKind.Other, "bad code");
        try
        {
            var response = await CallbackAsync(NewClient(factory), $"code=BAD&state={Uri.EscapeDataString(state)}", nonce);

            Assert.Contains("url=/admin/instagram?result=failed", await response.Content.ReadAsStringAsync());
            await using var scope = factory.Services.CreateAsyncScope();
            Assert.Equal(
                InstagramConnectionState.NotConnected,
                (await scope.ServiceProvider.GetRequiredService<IInstagramConnectionService>().GetStatusAsync()).State);
        }
        finally
        {
            factory.Instagram.Failure = null;
        }
    }

    [Fact]
    public async Task Callback_WithAWrongNonce_IsInvalid()
    {
        await ResetAsync();
        var admin = await AdminWithClientAsync("ig-nonce@example.com");
        var (state, _) = await StartAsync(admin.Client);

        var response = await CallbackAsync(NewClient(factory), $"code=C&state={Uri.EscapeDataString(state)}", "another-nonce");

        Assert.Contains("url=/admin/instagram?result=invalid", await response.Content.ReadAsStringAsync());
        await AssertNotConnectedAsync();
    }

    [Fact]
    public async Task Callback_WithoutTheCookie_IsInvalid()
    {
        await ResetAsync();
        var admin = await AdminWithClientAsync("ig-nocookie@example.com");
        var (state, _) = await StartAsync(admin.Client);

        var response = await CallbackAsync(NewClient(factory), $"code=C&state={Uri.EscapeDataString(state)}", null);

        Assert.Contains("url=/admin/instagram?result=invalid", await response.Content.ReadAsStringAsync());
        await AssertNotConnectedAsync();
    }

    [Theory]
    [InlineData("")]
    [InlineData("&state=garbage")]
    public async Task Callback_WithMissingOrTamperedState_IsInvalid_AndAudited(string stateParameter)
    {
        await ResetAsync();

        var response = await CallbackAsync(NewClient(factory), "code=C" + stateParameter, "nonce");

        Assert.Contains("url=/admin/instagram?result=invalid", await response.Content.ReadAsStringAsync());
        Assert.Contains(ReadLogLines(factory.LogsPath, "audit"), l => l.Contains("|instagram-connect-failed|user=-|") && l.Contains("reason=invalid-state"));
        await AssertNotConnectedAsync();
    }

    [Fact]
    public async Task Callback_WithoutACodeButValidState_IsInvalid()
    {
        await ResetAsync();
        var admin = await AdminWithClientAsync("ig-nocode@example.com");
        var (state, nonce) = await StartAsync(admin.Client);

        var response = await CallbackAsync(NewClient(factory), $"state={Uri.EscapeDataString(state)}", nonce);

        Assert.Contains("url=/admin/instagram?result=invalid", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Callback_AfterTenMinutes_IsExpired()
    {
        await ResetAsync();
        var admin = await AdminWithClientAsync("ig-expired@example.com");
        var (state, nonce) = await StartAsync(admin.Client);
        factory.Time.Advance(TimeSpan.FromMinutes(11));

        var response = await CallbackAsync(NewClient(factory), $"code=C&state={Uri.EscapeDataString(state)}", nonce);

        Assert.Contains("url=/admin/instagram?result=expired", await response.Content.ReadAsStringAsync());
        await AssertNotConnectedAsync();
    }

    [Fact]
    public async Task Callback_WithAMetaError_IsDenied_AndClearsTheCookie()
    {
        await ResetAsync();
        var admin = await AdminWithClientAsync("ig-denied@example.com");
        var (state, nonce) = await StartAsync(admin.Client);

        var response = await CallbackAsync(
            NewClient(factory),
            $"error=access_denied&error_reason=user_denied&error_description=The+user+denied+your+request&state={Uri.EscapeDataString(state)}",
            nonce);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("url=/admin/instagram?result=denied", html);
        Assert.DoesNotContain("denied your request", html);
        Assert.Contains("expires=", SetCookie(response, CookieName)!.ToLowerInvariant());
        Assert.Contains(ReadLogLines(factory.LogsPath, "audit"), l => l.Contains("|instagram-connect-failed|") && l.Contains("reason=denied"));
    }

    [Fact]
    public async Task Disconnect_WithoutAntiforgery_IsRejected()
    {
        var client = await LoggedInClientAsync("ig-csrf@example.com");

        var response = await client.PostAsync(Page + "?handler=Disconnect", new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Disconnect_WithAntiforgery_ClearsTheConnection()
    {
        await ResetAsync();
        var admin = await AdminWithClientAsync("ig-disconnect@example.com");
        var (state, nonce) = await StartAsync(admin.Client);
        await CallbackAsync(NewClient(factory), $"code=C&state={Uri.EscapeDataString(state)}", nonce);

        var response = await PostHandlerAsync(admin.Client, "Disconnect");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/admin/instagram", Location(response));
        await AssertNotConnectedAsync();
        Assert.Contains(ReadLogLines(factory.LogsPath, "audit"), l => l.Contains($"|instagram-disconnected|user={admin.Email}|"));
    }

    [Fact]
    public async Task Page_Connected_ShowsFetchNow_AndDisconnected_DoesNot()
    {
        await ResetAsync();
        var admin = await AdminWithClientAsync("ig-fetchbutton@example.com");
        Assert.DoesNotContain("Fetch now", await (await admin.Client.GetAsync(Page)).Content.ReadAsStringAsync());
        var (state, nonce) = await StartAsync(admin.Client);
        await CallbackAsync(NewClient(factory), $"code=C&state={Uri.EscapeDataString(state)}", nonce);

        var connected = await (await admin.Client.GetAsync(Page)).Content.ReadAsStringAsync();
        await SetSettingsAsync((InstagramSettingKeys.TokenInvalidAt, "2026-11-13T11:00:00.0000000Z"));
        var expired = await (await admin.Client.GetAsync(Page)).Content.ReadAsStringAsync();

        Assert.Contains("Fetch now", connected);
        Assert.Contains("handler=Fetch", connected);
        Assert.DoesNotContain("Fetch now", expired);
    }

    [Fact]
    public async Task Fetch_WithoutAntiforgery_IsRejected()
    {
        var client = await LoggedInClientAsync("ig-fetch-csrf@example.com");

        var response = await client.PostAsync(Page + "?handler=Fetch", new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Fetch_Anonymous_IsRedirectedToLogin()
    {
        var response = await NewClient(factory).PostAsync(Page + "?handler=Fetch", new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/admin/login", Location(response));
    }

    [Fact]
    public async Task Fetch_WhenConnected_Enqueues_Audits_AndShowsTheBanner()
    {
        await ResetAsync();
        var admin = await AdminWithClientAsync("ig-fetch@example.com");
        var (state, nonce) = await StartAsync(admin.Client);
        factory.JobQueue.Reset();
        await CallbackAsync(NewClient(factory), $"code=C&state={Uri.EscapeDataString(state)}", nonce);
        Assert.Equal(1, factory.JobQueue.FetchCount);
        factory.JobQueue.Reset();

        var response = await PostHandlerAsync(admin.Client, "Fetch");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/admin/instagram?result=fetch-queued", Location(response));
        Assert.Equal(1, factory.JobQueue.FetchCount);
        Assert.Contains(ReadLogLines(factory.LogsPath, "audit"), l => l.Contains($"|instagram-fetch-requested|user={admin.Email}|"));
        var html = await (await admin.Client.GetAsync(Location(response))).Content.ReadAsStringAsync();
        Assert.Contains("role=\"status\"", html);
        Assert.Contains("Fetching the latest posts. Reload this page in a minute to see the result.", html);
    }

    [Fact]
    public async Task Fetch_WhenNotConnected_EnqueuesNothing()
    {
        await ResetAsync();
        var client = await LoggedInClientAsync("ig-fetch-notconnected@example.com");
        factory.JobQueue.Reset();

        var response = await PostHandlerAsync(client, "Fetch");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/admin/instagram", Location(response));
        Assert.Equal(0, factory.JobQueue.FetchCount);
    }

    [Theory]
    [InlineData("ok:7", "Last fetch: 13 Nov 2026, 12:00 — 7 posts")]
    [InlineData("ok:1", "Last fetch: 13 Nov 2026, 12:00 — 1 post<")]
    [InlineData("failed:TokenInvalid", "Instagram rejected the access, please reconnect")]
    [InlineData("failed:Transient", "Instagram was not reachable, will retry at the next scheduled fetch")]
    [InlineData("failed:Other", "Instagram returned an error")]
    [InlineData("skipped:not-connected", "skipped, not connected")]
    [InlineData("skipped:no-posts", "Instagram returned no posts; the last posts were kept")]
    public async Task Status_ShowsTheLastFetchWithAMappedText(string result, string expected)
    {
        await ResetAsync();
        var client = await LoggedInClientAsync($"ig-status-{Guid.NewGuid():N}@example.com");
        await SetSettingsAsync(
            (InstagramSettingKeys.LastFetchAt, "2026-11-13T10:00:00.0000000Z"),
            (InstagramSettingKeys.LastFetchResult, result));
        try
        {
            var html = await (await client.GetAsync(Page)).Content.ReadAsStringAsync();

            Assert.Contains("<h2>Status</h2>", html);
            Assert.Contains("Posts stored: 0.", html);
            Assert.Contains(expected, html);
            Assert.DoesNotContain("Last access renewal", html);
        }
        finally
        {
            await RemoveSettingsAsync(InstagramSettingKeys.LastFetchAt, InstagramSettingKeys.LastFetchResult);
        }
    }

    [Theory]
    [InlineData("ok", "renewed")]
    [InlineData("skipped:too-recent", "not needed yet")]
    [InlineData("failed:TokenInvalid", "Instagram rejected the access, please reconnect")]
    [InlineData("skipped:not-connected", "skipped, not connected")]
    public async Task Status_ShowsTheLastRenewalWithAMappedText(string result, string text)
    {
        await ResetAsync();
        var client = await LoggedInClientAsync($"ig-renewal-{Guid.NewGuid():N}@example.com");
        await SetSettingsAsync(
            (InstagramSettingKeys.LastRefreshAt, "2026-07-13T10:00:00.0000000Z"),
            (InstagramSettingKeys.LastRefreshResult, result));
        try
        {
            var html = await (await client.GetAsync(Page)).Content.ReadAsStringAsync();

            Assert.Contains($"Last access renewal: 13 Jul 2026, 13:00 — {text}", html);
            Assert.DoesNotContain("Last fetch", html);
        }
        finally
        {
            await RemoveSettingsAsync(InstagramSettingKeys.LastRefreshAt, InstagramSettingKeys.LastRefreshResult);
        }
    }

    [Fact]
    public async Task Status_HasNoActivityLines_WhenNothingHasHappened()
    {
        await ResetAsync();
        var client = await LoggedInClientAsync("ig-nostatus@example.com");

        var html = await (await client.GetAsync(Page)).Content.ReadAsStringAsync();

        Assert.Contains("<h2>Status</h2>", html);
        Assert.DoesNotContain("Posts stored", html);
    }

    [Fact]
    public async Task Status_ScheduleLine_FollowsTheSectionState()
    {
        var client = await LoggedInClientAsync($"ig-schedule-{Guid.NewGuid():N}@example.com");
        try
        {
            await SetSectionActiveAsync(true);
            var on = await (await client.GetAsync(Page)).Content.ReadAsStringAsync();
            await SetSectionActiveAsync(false);
            var off = await (await client.GetAsync(Page)).Content.ReadAsStringAsync();

            Assert.Contains("Scheduled fetches: on (06:00 and 18:00, access renewal Mondays 04:00).", on);
            Assert.DoesNotContain("Scheduled fetches: off", on);
            Assert.Contains("Scheduled fetches: off (the Instagram feed section is hidden on the home page).", off);
            Assert.DoesNotContain("Scheduled fetches: on", off);
        }
        finally
        {
            await SetSectionActiveAsync(true);
        }
    }

    [Fact]
    public async Task Status_ScheduleLine_IsOn_WhenTheSectionHasNoRow()
    {
        var client = await LoggedInClientAsync($"ig-schedule-norow-{Guid.NewGuid():N}@example.com");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<ContentSection>().ExecuteDeleteAsync();
        }

        var html = await (await client.GetAsync(Page)).Content.ReadAsStringAsync();

        Assert.Contains("Scheduled fetches: on", html);
    }

    private async Task SetSectionActiveAsync(bool active)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IContentService>().ApplyChangesAsync(
            [new SetSectionActive(InstagramSection.Key, active)], new ContentEditor("ig-schedule@example.com", null));
        Assert.True(result.Succeeded);
    }

    private const string NotConnectedText = "Instagram not connected — connect";
    private const string DisconnectedText = "Instagram disconnected — reconnect";

    [Fact]
    public async Task Toolbar_NotConnected_ShowsTheWarning_OutsideTheMorePanel()
    {
        await ResetAsync();
        await SetSectionActiveAsync(true);
        var client = await LoggedInClientAsync($"ig-warn-nc-{Guid.NewGuid():N}@example.com");

        var html = await client.GetStringAsync("/");

        Assert.Contains($"<a class=\"cms-toolbar-warning\" href=\"/admin/instagram\"><span aria-hidden=\"true\">⚠ </span>{NotConnectedText}</a>", html);
        Assert.DoesNotContain(DisconnectedText, html);
        var warning = html.IndexOf("cms-toolbar-warning", StringComparison.Ordinal);
        var panelStart = html.IndexOf("id=\"cms-toolbar-more\"", StringComparison.Ordinal);
        var panelEnd = html.IndexOf("</form>\n    </div>", panelStart, StringComparison.Ordinal);
        Assert.True(panelEnd > panelStart);
        Assert.True(warning > panelEnd);
        Assert.True(warning < html.IndexOf("data-cms-status", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Toolbar_Disconnected_ShowsTheReconnectWarning_AndConnected_ShowsNone()
    {
        await ResetAsync();
        await SetSectionActiveAsync(true);
        var admin = await AdminWithClientAsync($"ig-warn-dc-{Guid.NewGuid():N}@example.com");
        var (state, nonce) = await StartAsync(admin.Client);
        await CallbackAsync(NewClient(factory), $"code=C&state={Uri.EscapeDataString(state)}", nonce);
        try
        {
            var connected = await admin.Client.GetStringAsync("/");
            await SetSettingsAsync((InstagramSettingKeys.TokenInvalidAt, "2026-11-13T11:00:00.0000000Z"));
            var disconnected = await admin.Client.GetStringAsync("/");

            Assert.DoesNotContain("cms-toolbar-warning", connected);
            Assert.Contains($"<span aria-hidden=\"true\">⚠ </span>{DisconnectedText}</a>", disconnected);
            Assert.DoesNotContain(NotConnectedText, disconnected);
        }
        finally
        {
            await ResetAsync();
        }
    }

    [Fact]
    public async Task Toolbar_SectionInactive_ShowsNoWarning_ButStillLinksToInstagram()
    {
        await ResetAsync();
        var client = await LoggedInClientAsync($"ig-warn-off-{Guid.NewGuid():N}@example.com");
        try
        {
            await SetSectionActiveAsync(false);

            var html = await client.GetStringAsync("/");

            Assert.DoesNotContain("cms-toolbar-warning", html);
            Assert.Contains("href=\"/admin/instagram\">Instagram</a>", html);
        }
        finally
        {
            await SetSectionActiveAsync(true);
        }
    }

    [Fact]
    public async Task Toolbar_InstagramLink_IsInTheMorePanel_AfterJobs()
    {
        var client = await LoggedInClientAsync($"ig-link-{Guid.NewGuid():N}@example.com");

        var html = await client.GetStringAsync("/");

        var panelStart = html.IndexOf("id=\"cms-toolbar-more\"", StringComparison.Ordinal);
        var jobs = html.IndexOf("<a class=\"cms-toolbar-link\" href=\"/admin/jobs\">Jobs</a>", StringComparison.Ordinal);
        var link = html.IndexOf("<a class=\"cms-toolbar-link\" href=\"/admin/instagram\">Instagram</a>", StringComparison.Ordinal);
        Assert.True(jobs > panelStart);
        Assert.True(link > jobs);
        Assert.True(link < html.IndexOf("action=\"/admin/logout\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Toolbar_Visitors_GetNeitherTheToolbarNorTheWarning()
    {
        await ResetAsync();
        await SetSectionActiveAsync(true);

        var html = await NewClient(factory).GetStringAsync("/");

        Assert.DoesNotContain("cms-toolbar", html);
        Assert.DoesNotContain(NotConnectedText, html);
        Assert.DoesNotContain("/admin/instagram", html);
    }

    [Fact]
    public async Task JobClasses_AreResolvableFromTheContainer()
    {
        await using var scope = factory.Services.CreateAsyncScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<InstagramFetchJob>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<InstagramTokenRefreshJob>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IInstagramFeedService>());
    }

    private async Task SetSettingsAsync(params (string Key, string Value)[] values)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<IIntegrationSettingRepository>();
        foreach (var (key, value) in values)
        {
            await settings.SetAsync(key, value);
        }

        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    private async Task RemoveSettingsAsync(params string[] keys)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<IIntegrationSettingRepository>();
        foreach (var key in keys)
        {
            await settings.RemoveAsync(key);
        }

        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    private async Task ResetAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IInstagramConnectionService>().DisconnectAsync("reset@example.com", null);
    }

    private async Task AssertNotConnectedAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var status = await scope.ServiceProvider.GetRequiredService<IInstagramConnectionService>().GetStatusAsync();
        Assert.Equal(InstagramConnectionState.NotConnected, status.State);
        var settings = scope.ServiceProvider.GetRequiredService<IIntegrationSettingRepository>();
        Assert.Null(await settings.GetAsync(InstagramSettingKeys.AccessToken));
    }

    private async Task<HttpClient> LoggedInClientAsync(string email) => (await AdminWithClientAsync(email)).Client;

    private async Task<(HttpClient Client, string Email)> AdminWithClientAsync(string email)
    {
        var client = NewClient(factory);
        var admin = await AddTwoFactorAdminAsync(factory, email);
        await LoginWithTotpAsync(client, admin);
        await EnsureAppAsync();
        return (client, email);
    }

    private static async Task<(string State, string Nonce)> StartAsync(HttpClient client)
    {
        var response = await PostHandlerAsync(client, "Connect");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var state = HttpUtility.ParseQueryString(new Uri(Location(response)).Query)["state"]!;
        var cookie = SetCookie(response, CookieName)!;
        var nonce = cookie[(CookieName.Length + 1)..cookie.IndexOf(';')];
        return (state, nonce);
    }

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string handler, Dictionary<string, string> fields)
    {
        var form = await client.GetAsync(Page);
        form.EnsureSuccessStatusCode();
        var data = new Dictionary<string, string>(fields)
        {
            ["__RequestVerificationToken"] = ExtractToken(await form.Content.ReadAsStringAsync()),
        };
        return await client.PostAsync($"{Page}?handler={handler}", new FormUrlEncodedContent(data));
    }

    private async Task EnsureAppAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IInstagramAppSettingsService>();
        if (!(await service.GetStatusAsync()).IsConfigured)
        {
            Assert.True((await service.SaveAsync("1234567890", SecretValue, false, "ensure@example.com", null)).Succeeded);
        }
    }

    private async Task RemoveAppAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IInstagramAppSettingsService>().RemoveAsync("reset@example.com", null);
    }

    private async Task<InstagramAppStatus> AppStatusAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IInstagramAppSettingsService>().GetStatusAsync();
    }

    private async Task<InstagramStatus> ConnectionStatusAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IInstagramConnectionService>().GetStatusAsync();
    }

    private static async Task<HttpResponseMessage> PostHandlerAsync(HttpClient client, string handler)
    {
        var form = await client.GetAsync(Page);
        form.EnsureSuccessStatusCode();
        var data = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractToken(await form.Content.ReadAsStringAsync()),
        };
        return await client.PostAsync($"{Page}?handler={handler}", new FormUrlEncodedContent(data));
    }

    private static Task<HttpResponseMessage> CallbackAsync(HttpClient client, string query, string? nonce)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/admin/instagram/callback?{query}");
        if (nonce is not null)
        {
            request.Headers.Add("Cookie", $"{CookieName}={nonce}");
        }

        return client.SendAsync(request);
    }

    private static string? SetCookie(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(v => v.StartsWith(name + "=", StringComparison.Ordinal))
            : null;
}
