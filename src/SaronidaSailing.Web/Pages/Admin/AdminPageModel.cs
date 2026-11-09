using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SaronidaSailing.Web.Pages.Admin;

public abstract class AdminPageModel : PageModel
{
    protected string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    protected string LocalReturnUrl(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/";

    protected static string NormalizeCode(string? code) =>
        (code ?? "").Replace(" ", "").Replace("-", "");
}
