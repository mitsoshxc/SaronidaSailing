using System.Net;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
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
}
