using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Web.Identity;
using static SaronidaSailing.Tests.AdminTestHelpers;

namespace SaronidaSailing.Tests;

[Collection(WebHostCollection.Name)]
public class AdminAuthTests(TempDatabaseWebApplicationFactory factory) : IClassFixture<TempDatabaseWebApplicationFactory>
{
    [Fact]
    public async Task LoginPage_IsOkAndNoindex_AndHasNoEnglishRoute()
    {
        var client = NewClient(factory);

        var login = await client.GetAsync("/admin/login");
        var english = await client.GetAsync("/en/admin/login");
        var home = await client.GetAsync("/");
        var englishHome = await client.GetAsync("/en/");

        var html = await login.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains("<meta name=\"robots\" content=\"noindex, nofollow\"", html);
        Assert.Contains("<html lang=\"en\">", html);
        Assert.DoesNotContain("hreflang", html);
        Assert.Equal(HttpStatusCode.NotFound, english.StatusCode);
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        Assert.Equal(HttpStatusCode.OK, englishHome.StatusCode);
    }

    [Fact]
    public async Task WrongPassword_ShowsGenericError_AndAuditsWithoutThePassword()
    {
        var client = NewClient(factory);
        await AddAdminAsync(factory, "wrongpw@example.com");
        const string attempt = "not-the-password-9137";

        var response = await LoginAsync(client, "wrongpw@example.com", attempt);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Invalid email or password, or the account is temporarily locked.", html);
        Assert.Contains("role=\"alert\"", html);
        var audit = ReadLogLines(factory.LogsPath, "audit");
        Assert.Contains(audit, l => l.Contains("|login-failed|user=wrongpw@example.com|"));
        Assert.DoesNotContain(audit, l => l.Contains(attempt));
    }

    [Fact]
    public async Task UnknownEmail_ShowsTheSameGenericError()
    {
        var client = NewClient(factory);

        var response = await LoginAsync(client, "nobody@example.com");

        Assert.Contains("Invalid email or password, or the account is temporarily locked.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task LockedOutAccount_ShowsTheSameMessage_AndAuditsLockout()
    {
        var client = NewClient(factory);
        await AddAdminAsync(factory, "lockout@example.com");

        for (var i = 0; i < 5; i++)
        {
            await LoginAsync(client, "lockout@example.com", "wrong-password-" + i);
        }

        var response = await LoginAsync(client, "lockout@example.com");

        Assert.Contains("Invalid email or password, or the account is temporarily locked.", await response.Content.ReadAsStringAsync());
        Assert.Contains(ReadLogLines(factory.LogsPath, "audit"), l => l.Contains("|login-locked-out|user=lockout@example.com|"));
    }

    [Fact]
    public async Task FirstLogin_RedirectsToSetup_WhichEnablesTwoFactorAndShowsRecoveryCodes()
    {
        var client = NewClient(factory);
        await AddAdminAsync(factory, "setup@example.com");

        var login = await LoginAsync(client, "setup@example.com");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/admin/two-factor/setup", Location(login));

        var setup = await client.GetAsync("/admin/two-factor/setup");
        var html = await setup.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        Assert.Contains("<svg role=\"img\" aria-label=", html);
        var key = Regex.Match(html, "<code class=\"admin-key\">([^<]+)</code>").Groups[1].Value;
        Assert.Matches("^([A-Z2-7]{4} ){3,}[A-Z2-7]{1,4}$", key);
        var plainKey = key.Replace(" ", "");

        var wrong = await PostFormAsync(client, "/admin/two-factor/setup", ("Code", Totp.Invalid(plainKey)));
        var wrongHtml = await wrong.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
        Assert.Contains("Invalid code.", wrongHtml);
        Assert.DoesNotContain("admin-codes", wrongHtml);
        Assert.Contains(ReadLogLines(factory.LogsPath, "audit"), l => l.Contains("|2fa-setup-failed|user=setup@example.com|"));

        var right = await PostFormAsync(client, "/admin/two-factor/setup", ("Code", Totp.Current(plainKey)));
        var rightHtml = await right.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, right.StatusCode);
        Assert.Equal(10, Regex.Matches(rightHtml, "<li><code>[^<]+</code></li>").Count);
        Assert.Contains("will not be shown again", rightHtml);
        Assert.Contains("no-store", right.Headers.CacheControl?.ToString());
        Assert.Contains(ReadLogLines(factory.LogsPath, "audit"), l => l.Contains("|2fa-enabled|user=setup@example.com|"));

        using var scope = factory.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync("setup@example.com");
        Assert.True(user!.TwoFactorEnabled);
        Assert.Equal(10, await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().CountRecoveryCodesAsync(user));

        var again = await client.GetAsync("/admin/two-factor/setup");
        Assert.Equal(HttpStatusCode.Redirect, again.StatusCode);
        Assert.Equal("/", Location(again));
        var login2 = await client.GetAsync("/admin/login");
        Assert.Equal(HttpStatusCode.Redirect, login2.StatusCode);
        Assert.Equal("/", Location(login2));
    }

    [Fact]
    public async Task SetupPage_WithoutLogin_ChallengesToLogin()
    {
        var client = NewClient(factory);

        var response = await client.GetAsync("/admin/two-factor/setup");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/admin/login", Location(response));
    }

    [Fact]
    public async Task AdminPolicy_RequiresMfaClaim()
    {
        using var _ = NewClient(factory);
        using var scope = factory.Services.CreateScope();
        await AddAdminAsync(factory, "policy@example.com");
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var signIn = scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();
        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        var user = (await users.FindByEmailAsync("policy@example.com"))!;
        var passwordOnly = await signIn.CreateUserPrincipalAsync(user);
        var identity = (ClaimsIdentity)passwordOnly.Identity!;
        var withMfa = new ClaimsPrincipal(new ClaimsIdentity(identity.Claims.Append(new Claim("amr", "mfa")), identity.AuthenticationType));

        var denied = await authorization.AuthorizeAsync(passwordOnly, null, AdminPolicy.Name);
        var allowed = await authorization.AuthorizeAsync(withMfa, null, AdminPolicy.Name);
        var anonymous = await authorization.AuthorizeAsync(new ClaimsPrincipal(new ClaimsIdentity()), null, AdminPolicy.Name);

        Assert.False(denied.Succeeded);
        Assert.True(allowed.Succeeded);
        Assert.False(anonymous.Succeeded);
    }

    [Fact]
    public async Task CodePage_WithoutPasswordStep_RedirectsToLogin()
    {
        var client = NewClient(factory);

        var get = await client.GetAsync("/admin/login/code");
        var recovery = await client.GetAsync("/admin/login/recovery");

        Assert.Equal(HttpStatusCode.Redirect, get.StatusCode);
        Assert.Equal("/admin/login", Location(get));
        Assert.Equal("/admin/login", Location(recovery));
    }

    [Fact]
    public async Task TwoFactorLogin_PasswordThenCode_RedirectsToLocalReturnUrl()
    {
        var client = NewClient(factory);
        var admin = await AddTwoFactorAdminAsync(factory, "totp@example.com");

        var password = await LoginAsync(client, admin.Email, url: "/admin/login?returnUrl=%2Fen%2F");
        Assert.Equal(HttpStatusCode.Redirect, password.StatusCode);
        Assert.Equal("/admin/login/code?returnUrl=%2Fen%2F", Location(password));

        var page = await client.GetAsync("/admin/login/code?returnUrl=%2Fen%2F");
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("inputmode=\"numeric\"", html);
        Assert.Contains("autocomplete=\"one-time-code\"", html);

        var wrong = await PostFormAsync(client, "/admin/login/code?returnUrl=%2Fen%2F", ("Code", Totp.Invalid(admin.Key)));
        Assert.Contains("Invalid code, or the account is temporarily locked.", await wrong.Content.ReadAsStringAsync());

        var code = Totp.Current(admin.Key);
        var ok = await PostFormAsync(client, "/admin/login/code?returnUrl=%2Fen%2F", ("Code", code.Insert(3, " ")));
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        Assert.Equal("/en/", Location(ok));
        Assert.Contains(ReadLogLines(factory.LogsPath, "audit"), l => l.Contains("|login-succeeded|user=totp@example.com|") && l.EndsWith("|totp"));
        Assert.Contains(ReadLogLines(factory.LogsPath, "audit"), l => l.Contains("|login-2fa-failed|user=totp@example.com|"));
    }

    [Fact]
    public async Task TwoFactorLogin_ExternalReturnUrl_FallsBackToRoot()
    {
        var client = NewClient(factory);
        var admin = await AddTwoFactorAdminAsync(factory, "external@example.com");
        const string url = "/admin/login/code?returnUrl=https%3A%2F%2Fevil.example%2F";
        await LoginAsync(client, admin.Email);

        var ok = await PostFormAsync(client, url, ("Code", Totp.Current(admin.Key)));

        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        Assert.Equal("/", Location(ok));
    }

    [Fact]
    public async Task RecoveryCode_LogsInOnce()
    {
        var admin = await AddTwoFactorAdminAsync(factory, "recovery@example.com");
        var client = NewClient(factory);
        await LoginAsync(client, admin.Email);

        var page = await client.GetAsync("/admin/login/recovery");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var ok = await PostFormAsync(client, "/admin/login/recovery", ("RecoveryCode", admin.RecoveryCodes[0]));
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        Assert.Equal("/", Location(ok));
        Assert.Contains(ReadLogLines(factory.LogsPath, "audit"), l => l.Contains("|recovery-code-used|user=recovery@example.com|") && l.EndsWith("|remaining=9"));
        Assert.DoesNotContain(ReadLogLines(factory.LogsPath, "audit"), l => l.Contains(admin.RecoveryCodes[0]));

        var second = NewClient(factory);
        await LoginAsync(second, admin.Email);
        var reuse = await PostFormAsync(second, "/admin/login/recovery", ("RecoveryCode", admin.RecoveryCodes[0]));

        Assert.Equal(HttpStatusCode.OK, reuse.StatusCode);
        Assert.Contains("Invalid code, or the account is temporarily locked.", await reuse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Logout_Post_SignsOut()
    {
        var client = NewClient(factory);
        var admin = await AddTwoFactorAdminAsync(factory, "logout@example.com");
        await LoginWithTotpAsync(client, admin);

        var page = await client.GetAsync("/admin/logout");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("<button type=\"submit\"", await page.Content.ReadAsStringAsync());

        var logout = await PostFormAsync(client, "/admin/logout");
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Equal("/", Location(logout));

        var setup = await client.GetAsync("/admin/two-factor/setup");
        Assert.Equal(HttpStatusCode.Redirect, setup.StatusCode);
        Assert.Contains("/admin/login", Location(setup));
        Assert.Contains(ReadLogLines(factory.LogsPath, "audit"), l => l.Contains("|logout|user=logout@example.com|"));
    }

    [Fact]
    public void AuthenticatorQr_BuildsUriAndFormatsKey()
    {
        var uri = AuthenticatorQr.BuildUri("a@b.gr", "ABCDEFGH");

        Assert.Equal("otpauth://totp/Saronida%20Sailing%3Aa%40b.gr?secret=ABCDEFGH&issuer=Saronida%20Sailing&digits=6", uri);
        Assert.Equal("ABCD EFGH IJ", AuthenticatorQr.FormatKey("abcdefghij"));
    }
}
