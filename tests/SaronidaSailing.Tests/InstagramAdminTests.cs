using System.Net;
using System.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Core.Services;
using static SaronidaSailing.Tests.AdminTestHelpers;

namespace SaronidaSailing.Tests;

public sealed class InstagramWebApplicationFactory : TempDatabaseWebApplicationFactory
{
    public FakeTimeProvider Time { get; } = new(DateTimeOffset.UtcNow);

    public FakeInstagramClient Instagram { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
            services.RemoveAll<IInstagramApiClient>();
            services.AddSingleton<IInstagramApiClient>(Instagram);
        });
    }
}

public sealed class FakeInstagramClient : IInstagramApiClient
{
    public const string LongToken = "LONG-LIVED-TOKEN-xyz";

    public bool IsConfigured { get; set; } = true;

    public InstagramApiException? Failure { get; set; }

    public string BuildAuthorizeUrl(string redirectUri, string state) =>
        $"https://www.instagram.com/oauth/authorize?client_id=app-1&redirect_uri={Uri.EscapeDataString(redirectUri)}&state={Uri.EscapeDataString(state)}";

    public Task<InstagramShortLivedToken> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct = default) =>
        Failure is null
            ? Task.FromResult(new InstagramShortLivedToken("SHORT", "17841400000000001"))
            : Task.FromException<InstagramShortLivedToken>(Failure);

    public Task<InstagramLongLivedToken> ExchangeForLongLivedAsync(string shortLivedToken, CancellationToken ct = default) =>
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
        factory.Instagram.IsConfigured = false;
        try
        {
            var html = await (await client.GetAsync(Page)).Content.ReadAsStringAsync();
            var post = await PostHandlerAsync(client, "Connect");
            var banner = await (await client.GetAsync(Location(post))).Content.ReadAsStringAsync();

            Assert.Contains("Instagram is not set up on this server (the Meta app ID or secret is missing).", html);
            Assert.Contains("disabled", html);
            Assert.Contains("aria-describedby=\"instagram-not-set-up\"", html);
            Assert.Equal("/admin/instagram?result=notconfigured", Location(post));
            Assert.Contains("role=\"alert\"", banner);
        }
        finally
        {
            factory.Instagram.IsConfigured = true;
        }
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
