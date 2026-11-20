using System.Net;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using SaronidaSailing.Core.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using static SaronidaSailing.Tests.AdminTestHelpers;

namespace SaronidaSailing.Tests;

[Collection(WebHostCollection.Name)]
public class AdminSecurityStampTests(TempDatabaseWebApplicationFactory factory) : IClassFixture<TempDatabaseWebApplicationFactory>
{
    [Fact]
    public async Task MfaClaim_SurvivesSecurityStampRevalidation()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var timed = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.Configure<SecurityStampValidatorOptions>(options => options.TimeProvider = clock);
            services.PostConfigure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, options => options.TimeProvider = clock);
        }));
        var client = NewClient(timed);
        var admin = await AddTwoFactorAdminAsync(timed, "stamp@example.com");
        await LoginWithTotpAsync(client, admin);

        clock.Advance(TimeSpan.FromMinutes(2));
        var response = await client.GetAsync("/admin/logout");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var refreshed = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("saronida.auth="));
        var value = refreshed["saronida.auth=".Length..refreshed.IndexOf(';')];
        var options = timed.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
        var ticket = options.TicketDataFormat.Unprotect(Uri.UnescapeDataString(value));
        Assert.NotNull(ticket);
        Assert.True(ticket.Principal.HasClaim("amr", "mfa"));
        Assert.True(ticket.Properties.IssuedUtc >= clock.GetUtcNow().AddSeconds(-1));
    }
    [Fact]
    public async Task Logout_ChangesTheSecurityStamp_AndEndsOtherSessionsOfTheSameAdmin()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var timed = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.Configure<SecurityStampValidatorOptions>(options => options.TimeProvider = clock);
            services.PostConfigure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, options => options.TimeProvider = clock);
        }));
        var admin = await AddTwoFactorAdminAsync(timed, "logout-stamp@example.com");
        var first = NewClient(timed);
        var second = NewClient(timed);
        await LoginWithTotpAsync(first, admin);
        await LoginWithTotpAsync(second, admin);
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync("/admin/logout")).StatusCode);
        var before = await StampAsync(timed, admin.Email);

        var logout = await PostFormAsync(first, "/admin/logout");

        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.NotEqual(before, await StampAsync(timed, admin.Email));
        clock.Advance(TimeSpan.FromMinutes(2));
        var other = await second.GetAsync("/admin/logout");
        Assert.Equal(HttpStatusCode.Redirect, other.StatusCode);
        Assert.Contains("/admin/login", Location(other));
        Assert.Contains(ReadLogLines(factory.LogsPath, "audit"), l => l.Contains("|logout|user=logout-stamp@example.com|"));
    }

    private static async Task<string?> StampAsync(WebApplicationFactory<Program> host, string email)
    {
        using var scope = host.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return (await users.FindByEmailAsync(email))!.SecurityStamp;
    }
}
