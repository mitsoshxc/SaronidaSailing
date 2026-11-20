using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;

namespace SaronidaSailing.Web.Pages.Admin;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class LogoutModel(SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager, IAuditLog audit) : AdminPageModel
{
    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var userName = User.Identity?.Name;
        var user = await userManager.GetUserAsync(User);
        if (user is not null)
        {
            await userManager.UpdateSecurityStampAsync(user);
        }

        await signInManager.SignOutAsync();
        audit.Record("logout", userName, ClientIp);
        return LocalRedirect("/");
    }
}
