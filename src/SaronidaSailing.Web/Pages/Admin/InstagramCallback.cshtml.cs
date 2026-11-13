using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Web.Instagram;

namespace SaronidaSailing.Web.Pages.Admin;

[AllowAnonymous]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class InstagramCallbackModel(
    InstagramOAuthState oauthState,
    IInstagramConnectionService connection,
    UserManager<ApplicationUser> users,
    IOptions<SiteOptions> site,
    IAuditLog audit) : AdminPageModel
{
    public string ContinueUrl { get; private set; } = InstagramPaths.Admin;

    public async Task<IActionResult> OnGetAsync(string? code, string? state, string? error, CancellationToken ct)
    {
        var result = await ResolveAsync(code, state, error, ct);
        ContinueUrl = InstagramPaths.ResultUrl(result);
        return Page();
    }

    private async Task<string> ResolveAsync(string? code, string? state, string? error, CancellationToken ct)
    {
        var validated = oauthState.Validate(HttpContext, state);
        if (!string.IsNullOrEmpty(error))
        {
            return Fail("denied", "denied");
        }

        switch (validated.Status)
        {
            case InstagramOAuthStatus.Expired:
                return Fail("expired", "expired");
            case InstagramOAuthStatus.Invalid:
                return Fail("invalid", "invalid-state");
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return Fail("invalid", "missing-code");
        }

        var user = await users.FindByIdAsync(validated.AdminUserId!);
        if (user is null || !user.TwoFactorEnabled || await users.IsLockedOutAsync(user) || string.IsNullOrEmpty(user.Email))
        {
            return Fail("invalid", "invalid-user");
        }

        var connectResult = await connection.CompleteConnectAsync(
            code, InstagramPaths.RedirectUri(site.Value.BaseUrl), user.Email, ClientIp, ct);
        return connectResult.Succeeded ? "connected" : "failed";
    }

    private string Fail(string result, string reason)
    {
        audit.Record("instagram-connect-failed", null, ClientIp, $"reason={reason}");
        return result;
    }
}
