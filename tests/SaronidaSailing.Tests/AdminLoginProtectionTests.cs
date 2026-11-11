using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Web;
using SaronidaSailing.Web.Identity;
using static SaronidaSailing.Tests.AdminTestHelpers;

namespace SaronidaSailing.Tests;

[Collection(WebHostCollection.Name)]
public class AdminLoginProtectionTests(TempDatabaseWebApplicationFactory factory) : IClassFixture<TempDatabaseWebApplicationFactory>
{
    private const string Generic = "Invalid email or password, or the account is temporarily locked.";

    [Theory]
    [InlineData("POST", "/admin/login", true)]
    [InlineData("post", "/ADMIN/Login/", true)]
    [InlineData("POST", "/admin/login/code", true)]
    [InlineData("POST", "/admin/login/recovery/", true)]
    [InlineData("POST", "/admin/two-factor/setup", true)]
    [InlineData("GET", "/admin/login", false)]
    [InlineData("POST", "/admin/logout", false)]
    [InlineData("POST", "/admin/login/other", false)]
    [InlineData("POST", "/", false)]
    [InlineData("POST", "/en/", false)]
    public void IsLimited_MatchesOnlyPostsToLoginPaths(string method, string path, bool expected) =>
        Assert.Equal(expected, AdminLoginRateLimit.IsLimited(method, new PathString(path)));

    [Fact]
    public void ForwardedHeadersSetup_InvalidCidr_FailsWithClearMessage()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ForwardedHeaders:KnownNetworks:0"] = "not-a-cidr" })
            .Build();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            { ForwardedHeadersSetup.Configure(new Microsoft.AspNetCore.Builder.ForwardedHeadersOptions(), config); });

        Assert.Contains("ForwardedHeaders:KnownNetworks", ex.Message);
        Assert.Contains("not-a-cidr", ex.Message);
    }

    [Fact]
    public async Task TrustedProxy_ForwardedFor_IsUsedAsClientIpInAudit()
    {
        using var client = NewClientFromIp(factory, "172.30.0.1");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.7");

        await LoginAsync(client, "fwd-trusted@example.com");

        Assert.Contains(ReadLogLines(factory.LogsPath, "audit"),
            l => l.Contains("|login-failed|user=fwd-trusted@example.com|ip=203.0.113.7"));
    }

    [Fact]
    public async Task UntrustedProxy_ForwardedFor_IsIgnored()
    {
        using var client = NewClientFromIp(factory, "10.9.9.9");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.8");

        await LoginAsync(client, "fwd-untrusted@example.com");

        Assert.Contains(ReadLogLines(factory.LogsPath, "audit"),
            l => l.Contains("|login-failed|user=fwd-untrusted@example.com|ip=10.9.9.9"));
    }

    [Theory]
    [InlineData("172.30.0.1", "https")]
    [InlineData("10.9.9.10", "http")]
    public async Task ForwardedProto_IsHonoredOnlyFromTrustedProxy(string remoteIp, string expectedScheme)
    {
        var schemes = new List<string>();
        using var recording = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Configure<MvcOptions>(options => options.Filters.Add(new SchemeRecorder(schemes)))));
        using var client = NewClientFromIp(recording, remoteIp, new Uri("http://localhost"));
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");

        var response = await client.GetAsync("/admin/login");

        // Over plain http the Secure-only antiforgery cookie makes the page fail after the handler ran, so only the trusted case is a 200.
        if (expectedScheme == "https")
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(expectedScheme, Assert.Single(schemes));
    }

    [Fact]
    public async Task RateLimit_EleventhPostFromOneIp_Gets429_OthersAndGetsStayAllowed()
    {
        const string ip = "10.30.0.1";
        using var client = NewClientFromIp(factory, ip);

        for (var i = 0; i < AdminLoginRateLimit.PermitLimit; i++)
        {
            var ok = await LoginAsync(client, "limited@example.com", "wrong-" + i);
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            Assert.Contains(Generic, await ok.Content.ReadAsStringAsync());
        }

        var limited = await LoginAsync(client, "limited@example.com", "wrong-final");

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("60", limited.Headers.GetValues("Retry-After").Single());
        Assert.Equal("text/html; charset=utf-8", limited.Content.Headers.ContentType!.ToString());
        var body = await limited.Content.ReadAsStringAsync();
        Assert.Contains("<title>Too many attempts — Saronida Sailing admin</title>", body);
        Assert.Contains("<h1>Too many attempts</h1>", body);
        Assert.Contains(AdminLoginRateLimit.RejectionMessage, body);
        Assert.Contains("href=\"/admin/login\"", body);
        Assert.Contains(ReadLogLines(factory.LogsPath, "audit"), l => l.Contains($"|login-rate-limited|user=-|ip={ip}|/admin/login"));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/admin/login")).StatusCode);

        using var other = NewClientFromIp(factory, "10.30.0.2");
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(other, "limited@example.com", "wrong")).StatusCode);
    }

    [Fact]
    public async Task RateLimit_PostsToOtherPaths_AreNeverLimited()
    {
        using var client = NewClientFromIp(factory, "10.30.0.3");

        for (var i = 0; i < AdminLoginRateLimit.PermitLimit + 5; i++)
        {
            var response = await client.PostAsync("/", new FormUrlEncodedContent(new Dictionary<string, string>()));
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
    }

    [Fact]
    public async Task RateLimit_UsesForwardedClientIpFromTrustedProxy()
    {
        using var client = NewClientFromIp(factory, "172.30.0.1");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.50");

        for (var i = 0; i < AdminLoginRateLimit.PermitLimit; i++)
        {
            await LoginAsync(client, "fwd-limit@example.com", "wrong-" + i);
        }

        var limited = await LoginAsync(client, "fwd-limit@example.com", "wrong");
        using var otherClientBehindProxy = NewClientFromIp(factory, "172.30.0.1");
        otherClientBehindProxy.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.51");
        var allowed = await LoginAsync(otherClientBehindProxy, "fwd-limit@example.com", "wrong");

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task Lockout_FiveWrongPasswords_LockEvenTheCorrectPassword_UntilItExpires()
    {
        const string email = "lock-password@example.com";
        using var client = NewClient(factory);
        await AddAdminAsync(factory, email);

        for (var i = 0; i < 5; i++)
        {
            await LoginAsync(client, email, "wrong-password-" + i);
        }

        var locked = await LoginAsync(client, email);
        Assert.Equal(HttpStatusCode.OK, locked.StatusCode);
        Assert.Contains(Generic, await locked.Content.ReadAsStringAsync());
        Assert.Contains(ReadLogLines(factory.LogsPath, "audit"), l => l.Contains($"|login-locked-out|user={email}|"));

        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByEmailAsync(email))!;
            Assert.True(user.LockoutEnd > DateTimeOffset.UtcNow.AddMinutes(14));
            await users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddMinutes(-1));
        }

        var afterExpiry = await LoginAsync(client, email);
        Assert.Equal(HttpStatusCode.Redirect, afterExpiry.StatusCode);
        Assert.Equal("/admin/two-factor/setup", Location(afterExpiry));
    }

    [Fact]
    public async Task Lockout_IsClearedByAdminResetPassword()
    {
        const string email = "lock-reset@example.com";
        const string newPassword = "another-long-passphrase-42";
        using var client = NewClient(factory);
        await AddAdminAsync(factory, email);
        for (var i = 0; i < 5; i++)
        {
            await LoginAsync(client, email, "wrong-password-" + i);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<IAdminAccountService>().ResetPasswordAsync(email, newPassword);
            Assert.True(result.Succeeded);
        }

        var response = await LoginAsync(client, email, newPassword);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/admin/two-factor/setup", Location(response));
    }

    [Fact]
    public async Task Lockout_FiveWrongTotpCodes_LockTheAccount()
    {
        using var client = NewClient(factory);
        var admin = await AddTwoFactorAdminAsync(factory, "lock-totp@example.com");
        var password = await LoginAsync(client, admin.Email);
        Assert.Equal(HttpStatusCode.Redirect, password.StatusCode);

        for (var i = 0; i < 5; i++)
        {
            await PostFormAsync(client, "/admin/login/code", ("Code", Totp.Invalid(admin.Key)));
        }

        var correct = await PostFormAsync(client, "/admin/login/code", ("Code", Totp.Current(admin.Key)));

        Assert.Equal(HttpStatusCode.OK, correct.StatusCode);
        Assert.Contains("Invalid code, or the account is temporarily locked.", await correct.Content.ReadAsStringAsync());
        Assert.Contains(ReadLogLines(factory.LogsPath, "audit"), l => l.Contains($"|login-locked-out|user={admin.Email}|"));
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.True(await users.IsLockedOutAsync((await users.FindByEmailAsync(admin.Email))!));
    }

    private sealed class SchemeRecorder(List<string> schemes) : IPageFilter
    {
        public void OnPageHandlerSelected(PageHandlerSelectedContext context) => schemes.Add(context.HttpContext.Request.Scheme);

        public void OnPageHandlerExecuting(PageHandlerExecutingContext context)
        {
        }

        public void OnPageHandlerExecuted(PageHandlerExecutedContext context)
        {
        }
    }
}
