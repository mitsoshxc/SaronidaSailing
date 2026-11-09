using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SaronidaSailing.Core.Entities;

namespace SaronidaSailing.Tests;

[Collection(WebHostCollection.Name)]
public class IdentityTests(TempDatabaseWebApplicationFactory factory) : IClassFixture<TempDatabaseWebApplicationFactory>
{
    [Fact]
    public async Task IdentityTables_ExistAfterStartup()
    {
        using var _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var result = await users.CreateAsync(
            new ApplicationUser { UserName = "tables@example.com", Email = "tables@example.com" },
            "alllowercase12");
        var found = await users.FindByEmailAsync("tables@example.com");

        Assert.True(result.Succeeded);
        Assert.Equal("tables@example.com", found?.UserName);
    }

    [Fact]
    public async Task Password_RequiresTwelveCharactersWithoutCharacterRules()
    {
        using var _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var tooShort = await users.CreateAsync(new ApplicationUser { UserName = "short@example.com", Email = "short@example.com" }, "abcdefghijk");
        var longEnough = await users.CreateAsync(new ApplicationUser { UserName = "long@example.com", Email = "long@example.com" }, "abcdefghijkl");

        Assert.False(tooShort.Succeeded);
        Assert.Contains(tooShort.Errors, e => e.Code == nameof(IdentityErrorDescriber.PasswordTooShort));
        Assert.True(longEnough.Succeeded);
    }

    [Fact]
    public void Lockout_IsFiveAttemptsForFifteenMinutes()
    {
        using var _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        Assert.Equal(5, users.Options.Lockout.MaxFailedAccessAttempts);
        Assert.Equal(TimeSpan.FromMinutes(15), users.Options.Lockout.DefaultLockoutTimeSpan);
        Assert.True(users.Options.Lockout.AllowedForNewUsers);
    }

    [Fact]
    public void DataProtection_WritesKeyToConfiguredFolder()
    {
        using var _ = factory.CreateClient();
        var protector = factory.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("test");

        protector.Protect("value");

        Assert.NotEmpty(Directory.GetFiles(factory.KeysPath, "key-*.xml"));
    }

    [Fact]
    public void ApplicationCookie_HasAgreedOptions()
    {
        using var _ = factory.CreateClient();
        var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);

        Assert.Equal("saronida.auth", options.Cookie.Name);
        Assert.Equal(TimeSpan.FromHours(12), options.ExpireTimeSpan);
        Assert.True(options.SlidingExpiration);
        Assert.True(options.Cookie.HttpOnly);
        Assert.Equal(CookieSecurePolicy.Always, options.Cookie.SecurePolicy);
        Assert.Equal(SameSiteMode.Strict, options.Cookie.SameSite);
        Assert.Equal("/admin/login", options.LoginPath.Value);
        Assert.Equal("/admin/logout", options.LogoutPath.Value);
        Assert.Equal("/admin/two-factor/setup", options.AccessDeniedPath.Value);
    }
}
