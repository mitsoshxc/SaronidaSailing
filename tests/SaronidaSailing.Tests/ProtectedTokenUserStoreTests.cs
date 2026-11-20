using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Infrastructure.Data;
using static SaronidaSailing.Tests.AdminTestHelpers;

namespace SaronidaSailing.Tests;

[Collection(WebHostCollection.Name)]
public class ProtectedTokenUserStoreTests(TempDatabaseWebApplicationFactory factory) : IClassFixture<TempDatabaseWebApplicationFactory>
{
    private const string Provider = "[AspNetUserStore]";

    [Fact]
    public async Task AuthenticatorKeyAndRecoveryCodes_AreEncryptedAtRest_AndReadBackThroughUserManager()
    {
        var admin = await AddTwoFactorAdminAsync(factory, "protected-tokens@example.com");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByEmailAsync(admin.Email))!;
        var rows = await db.UserTokens.Where(t => t.UserId == user.Id && t.LoginProvider == Provider).ToListAsync();
        var keyRow = rows.Single(t => t.Name == "AuthenticatorKey");
        var codesRow = rows.Single(t => t.Name == "RecoveryCodes");

        Assert.DoesNotContain(admin.Key, keyRow.Value);
        Assert.All(admin.RecoveryCodes, code => Assert.DoesNotContain(code, codesRow.Value));
        Assert.Equal(admin.Key, await users.GetAuthenticatorKeyAsync(user));
        Assert.Equal(10, await users.CountRecoveryCodesAsync(user));
    }

    [Fact]
    public async Task RecoveryCode_IsConsumedOnRedeem()
    {
        var admin = await AddTwoFactorAdminAsync(factory, "protected-redeem@example.com");

        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByEmailAsync(admin.Email))!;

        var first = await users.RedeemTwoFactorRecoveryCodeAsync(user, admin.RecoveryCodes[0]);
        var again = await users.RedeemTwoFactorRecoveryCodeAsync(user, admin.RecoveryCodes[0]);

        Assert.True(first.Succeeded);
        Assert.False(again.Succeeded);
        Assert.Equal(9, await users.CountRecoveryCodesAsync(user));
    }

    [Fact]
    public async Task PlainTextAuthenticatorKey_ReadsBackAsNotSet()
    {
        await AddAdminAsync(factory, "protected-plain@example.com");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByEmailAsync("protected-plain@example.com"))!;
        db.UserTokens.Add(new IdentityUserToken<string> { UserId = user.Id, LoginProvider = Provider, Name = "AuthenticatorKey", Value = "PLAINTEXTKEY234567" });
        await db.SaveChangesAsync();

        Assert.Null(await users.GetAuthenticatorKeyAsync(user));
    }

    [Fact]
    public async Task OtherTokens_RoundTripUnencrypted()
    {
        await AddAdminAsync(factory, "protected-other@example.com");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByEmailAsync("protected-other@example.com"))!;

        await users.SetAuthenticationTokenAsync(user, "SomeProvider", "SomeToken", "plain-value");

        var row = await db.UserTokens.SingleAsync(t => t.UserId == user.Id && t.LoginProvider == "SomeProvider");
        Assert.Equal("plain-value", row.Value);
        Assert.Equal("plain-value", await users.GetAuthenticationTokenAsync(user, "SomeProvider", "SomeToken"));
    }
}
