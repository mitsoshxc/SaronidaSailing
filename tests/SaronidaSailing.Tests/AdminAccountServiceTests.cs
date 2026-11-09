using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;

namespace SaronidaSailing.Tests;

[Collection(WebHostCollection.Name)]
public class AdminAccountServiceTests(TempDatabaseWebApplicationFactory factory) : IClassFixture<TempDatabaseWebApplicationFactory>
{
    private const string Password = "correct-horse-battery";

    private (IServiceScope Scope, IAdminAccountService Admins, UserManager<ApplicationUser> Users) Create()
    {
        using var _ = factory.CreateClient();
        var scope = factory.Services.CreateScope();
        return (scope, scope.ServiceProvider.GetRequiredService<IAdminAccountService>(), scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>());
    }

    [Fact]
    public async Task Add_CreatesUserWithTrimmedEmail()
    {
        var (scope, admins, users) = Create();
        using var _ = scope;

        var result = await admins.AddAsync("  add1@example.com ", Password);

        Assert.True(result.Succeeded);
        var user = await users.FindByNameAsync("add1@example.com");
        Assert.NotNull(user);
        Assert.Equal("add1@example.com", user.Email);
    }

    [Fact]
    public async Task Add_RejectsDuplicateShortPasswordAndInvalidEmail()
    {
        var (scope, admins, _) = Create();
        using var _s = scope;
        await admins.AddAsync("dup@example.com", Password);

        var duplicate = await admins.AddAsync("dup@example.com", Password);
        var shortPassword = await admins.AddAsync("shortpw@example.com", "short");
        var badEmail = await admins.AddAsync("not-an-email", Password);

        Assert.False(duplicate.Succeeded);
        Assert.NotEmpty(duplicate.Errors);
        Assert.False(shortPassword.Succeeded);
        Assert.False(badEmail.Succeeded);
    }

    [Fact]
    public async Task ResetPassword_ChangesPasswordAndStamp_ClearsLockout_KeepsTwoFactor()
    {
        var (scope, admins, users) = Create();
        using var _ = scope;
        await admins.AddAsync("reset@example.com", Password);
        var user = (await users.FindByEmailAsync("reset@example.com"))!;
        await users.SetTwoFactorEnabledAsync(user, true);
        await users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddMinutes(10));
        await users.AccessFailedAsync(user);
        var stampBefore = await users.GetSecurityStampAsync(user);

        var result = await admins.ResetPasswordAsync("reset@example.com", "another-long-password");

        Assert.True(result.Succeeded);
        user = (await users.FindByEmailAsync("reset@example.com"))!;
        Assert.True(await users.CheckPasswordAsync(user, "another-long-password"));
        Assert.False(await users.CheckPasswordAsync(user, Password));
        Assert.NotEqual(stampBefore, await users.GetSecurityStampAsync(user));
        Assert.Null(user.LockoutEnd);
        Assert.Equal(0, user.AccessFailedCount);
        Assert.True(user.TwoFactorEnabled);
    }

    [Fact]
    public async Task ResetPassword_RejectsShortPassword()
    {
        var (scope, admins, users) = Create();
        using var _ = scope;
        await admins.AddAsync("resetshort@example.com", Password);

        var result = await admins.ResetPasswordAsync("resetshort@example.com", "short");

        Assert.False(result.Succeeded);
        Assert.True(await users.CheckPasswordAsync((await users.FindByEmailAsync("resetshort@example.com"))!, Password));
    }

    [Fact]
    public async Task List_IsOrderedByEmail_AndShowsLockoutOnlyWhileLocked()
    {
        var (scope, admins, users) = Create();
        using var _ = scope;
        await admins.AddAsync("list-b@example.com", Password);
        await admins.AddAsync("list-a@example.com", Password);
        await admins.AddAsync("list-c@example.com", Password);
        var a = (await users.FindByEmailAsync("list-a@example.com"))!;
        await users.SetTwoFactorEnabledAsync(a, true);
        await users.SetLockoutEndDateAsync(a, DateTimeOffset.UtcNow.AddMinutes(10));
        var c = (await users.FindByEmailAsync("list-c@example.com"))!;
        await users.SetLockoutEndDateAsync(c, DateTimeOffset.UtcNow.AddMinutes(-10));

        var list = (await admins.ListAsync()).Where(s => s.Email.StartsWith("list-")).ToList();

        Assert.Equal(["list-a@example.com", "list-b@example.com", "list-c@example.com"], list.Select(s => s.Email));
        Assert.True(list[0].TwoFactorEnabled);
        Assert.NotNull(list[0].LockedOutUntilUtc);
        Assert.False(list[1].TwoFactorEnabled);
        Assert.Null(list[1].LockedOutUntilUtc);
        Assert.Null(list[2].LockedOutUntilUtc);
    }

    [Fact]
    public async Task Remove_DeletesUser()
    {
        var (scope, admins, users) = Create();
        using var _ = scope;
        await admins.AddAsync("remove@example.com", Password);

        var result = await admins.RemoveAsync("remove@example.com");

        Assert.True(result.Succeeded);
        Assert.Null(await users.FindByEmailAsync("remove@example.com"));
    }

    [Fact]
    public async Task UnknownEmail_FailsForResetAndRemove()
    {
        var (scope, admins, _) = Create();
        using var _s = scope;

        var reset = await admins.ResetPasswordAsync("nobody@example.com", Password);
        var remove = await admins.RemoveAsync("nobody@example.com");

        Assert.False(reset.Succeeded);
        Assert.Equal(["No admin with that email."], reset.Errors);
        Assert.False(remove.Succeeded);
        Assert.Equal(["No admin with that email."], remove.Errors);
    }
}
