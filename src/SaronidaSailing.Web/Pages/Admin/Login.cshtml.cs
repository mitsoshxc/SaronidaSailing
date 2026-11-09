using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Web.Identity;

namespace SaronidaSailing.Web.Pages.Admin;

[AllowAnonymous]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class LoginModel(SignInManager<ApplicationUser> signInManager, IAuditLog audit) : AdminPageModel
{
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [BindProperty, Required]
    public string? Email { get; set; }

    [BindProperty, Required]
    public string? Password { get; set; }

    public string? Error { get; private set; }

    public IActionResult OnGet() =>
        User.HasClaim(AdminPolicy.MfaClaimType, AdminPolicy.MfaClaimValue) ? LocalRedirect("/") : Page();

    public async Task<IActionResult> OnPostAsync()
    {
        var email = Email?.Trim();
        if (!ModelState.IsValid || string.IsNullOrEmpty(email))
        {
            Error = "Enter your email and password.";
            return Page();
        }

        var result = await signInManager.PasswordSignInAsync(email, Password!, isPersistent: false, lockoutOnFailure: true);

        if (result.RequiresTwoFactor)
        {
            audit.Record("login-2fa-required", email, ClientIp);
            return RedirectToPage("/Admin/LoginCode", new { returnUrl = ReturnUrl });
        }

        if (result.Succeeded)
        {
            audit.Record("login-2fa-setup-required", email, ClientIp);
            return LocalRedirect("/admin/two-factor/setup");
        }

        audit.Record(result.IsLockedOut ? "login-locked-out" : "login-failed", email, ClientIp);
        Error = "Invalid email or password, or the account is temporarily locked.";
        return Page();
    }
}
