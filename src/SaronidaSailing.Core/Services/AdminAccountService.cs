using Microsoft.AspNetCore.Identity;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;

namespace SaronidaSailing.Core.Services;

public sealed class AdminAccountService(UserManager<ApplicationUser> users, TimeProvider timeProvider) : IAdminAccountService
{
    private const string UnknownAdmin = "No admin with that email.";

    public async Task<AdminResult> AddAsync(string email, string password)
    {
        var trimmed = email.Trim();
        var result = await users.CreateAsync(new ApplicationUser { UserName = trimmed, Email = trimmed }, password);
        return ToResult(result);
    }

    public async Task<AdminResult> ResetPasswordAsync(string email, string password)
    {
        var user = await users.FindByEmailAsync(email.Trim());
        if (user is null)
        {
            return AdminResult.Failure(UnknownAdmin);
        }

        var token = await users.GeneratePasswordResetTokenAsync(user);
        var reset = await users.ResetPasswordAsync(user, token, password);
        if (!reset.Succeeded)
        {
            return ToResult(reset);
        }

        var unlock = await users.SetLockoutEndDateAsync(user, null);
        if (!unlock.Succeeded)
        {
            return ToResult(unlock);
        }

        return ToResult(await users.ResetAccessFailedCountAsync(user));
    }

    public Task<IReadOnlyList<AdminSummary>> ListAsync()
    {
        var now = timeProvider.GetUtcNow();
        var all = users.Users.OrderBy(u => u.Email).ToList();
        IReadOnlyList<AdminSummary> summaries = all
            .Select(u => new AdminSummary(
                u.Email ?? u.UserName ?? "",
                u.TwoFactorEnabled,
                u.LockoutEnd is { } end && end > now ? end.UtcDateTime : null))
            .ToList();
        return Task.FromResult(summaries);
    }

    public async Task<AdminResult> RemoveAsync(string email)
    {
        var user = await users.FindByEmailAsync(email.Trim());
        if (user is null)
        {
            return AdminResult.Failure(UnknownAdmin);
        }

        return ToResult(await users.DeleteAsync(user));
    }

    private static AdminResult ToResult(IdentityResult result) =>
        result.Succeeded ? AdminResult.Success : new AdminResult(false, [.. result.Errors.Select(e => e.Description)]);
}
