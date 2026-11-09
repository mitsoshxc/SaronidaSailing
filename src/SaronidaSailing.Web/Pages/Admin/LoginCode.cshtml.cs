using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;

namespace SaronidaSailing.Web.Pages.Admin;

[AllowAnonymous]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class LoginCodeModel(SignInManager<ApplicationUser> signInManager, IAuditLog audit) : AdminPageModel
{
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [BindProperty, Required]
    public string? Code { get; set; }

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

        var code = NormalizeCode(Code);
        if (!ModelState.IsValid || code.Length == 0)
        {
            Error = "Enter the 6-digit code from your authenticator app.";
            return Page();
        }

        var result = await signInManager.TwoFactorAuthenticatorSignInAsync(code, isPersistent: false, rememberClient: false);
        if (result.Succeeded)
        {
            audit.Record("login-succeeded", user.UserName, ClientIp, "totp");
            return LocalRedirect(LocalReturnUrl(ReturnUrl));
        }

        audit.Record(result.IsLockedOut ? "login-locked-out" : "login-2fa-failed", user.UserName, ClientIp);
        Error = "Invalid code, or the account is temporarily locked.";
        return Page();
    }
}
