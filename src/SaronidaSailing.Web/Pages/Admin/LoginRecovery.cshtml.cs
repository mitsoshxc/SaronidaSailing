using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;

namespace SaronidaSailing.Web.Pages.Admin;

[AllowAnonymous]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class LoginRecoveryModel(
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    IAuditLog audit) : AdminPageModel
{
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [BindProperty, Required]
    public string? RecoveryCode { get; set; }

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync() =>
        await signInManager.GetTwoFactorAuthenticationUserAsync() is null ? RedirectToPage("/Admin/Login") : Page();

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await signInManager.GetTwoFactorAuthenticationUserAsync();
        if (user is null)
        {
            return RedirectToPage("/Admin/Login");
        }

        var code = (RecoveryCode ?? "").Replace(" ", "");
        if (!ModelState.IsValid || code.Length == 0)
        {
            Error = "Enter one of your recovery codes.";
            return Page();
        }

        var result = await signInManager.TwoFactorRecoveryCodeSignInAsync(code);
        if (result.Succeeded)
        {
            var remaining = await userManager.CountRecoveryCodesAsync(user);
            audit.Record("login-succeeded", user.UserName, ClientIp, "recovery");
            audit.Record("recovery-code-used", user.UserName, ClientIp, $"remaining={remaining}");
            return LocalRedirect(LocalReturnUrl(ReturnUrl));
        }

        audit.Record(result.IsLockedOut ? "login-locked-out" : "login-2fa-failed", user.UserName, ClientIp);
        Error = "Invalid code, or the account is temporarily locked.";
        return Page();
    }
}
