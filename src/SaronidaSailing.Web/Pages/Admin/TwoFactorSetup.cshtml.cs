using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Web.Identity;

namespace SaronidaSailing.Web.Pages.Admin;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class TwoFactorSetupModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IAuditLog audit) : AdminPageModel
{
    [BindProperty]
    public string? Code { get; set; }

    public string QrSvg { get; private set; } = "";

    public string FormattedKey { get; private set; } = "";

    public string? Error { get; private set; }

    public IReadOnlyList<string>? RecoveryCodes { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return RedirectToPage("/Admin/Login");
        }

        if (user.TwoFactorEnabled)
        {
            return LocalRedirect("/");
        }

        await LoadKeyAsync(user);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return RedirectToPage("/Admin/Login");
        }

        if (user.TwoFactorEnabled)
        {
            return LocalRedirect("/");
        }

        var code = NormalizeCode(Code);
        var valid = code.Length > 0
            && await userManager.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, code);
        if (!valid)
        {
            audit.Record("2fa-setup-failed", user.UserName, ClientIp);
            Error = "Invalid code. Check the code in your authenticator app and try again.";
            await LoadKeyAsync(user);
            return Page();
        }

        await userManager.SetTwoFactorEnabledAsync(user, true);
        var codes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        await signInManager.SignInWithClaimsAsync(
            user,
            isPersistent: false,
            [new Claim(AdminPolicy.MfaClaimType, AdminPolicy.MfaClaimValue)]);
        audit.Record("2fa-enabled", user.UserName, ClientIp);

        RecoveryCodes = [.. codes ?? []];
        return Page();
    }

    private async Task LoadKeyAsync(ApplicationUser user)
    {
        var key = await userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key))
        {
            await userManager.ResetAuthenticatorKeyAsync(user);
            key = await userManager.GetAuthenticatorKeyAsync(user);
        }

        var uri = AuthenticatorQr.BuildUri(user.Email ?? user.UserName ?? "", key!);
        QrSvg = AuthenticatorQr.RenderSvg(uri, "QR code for the authenticator app");
        FormattedKey = AuthenticatorQr.FormatKey(key!);
    }
}
