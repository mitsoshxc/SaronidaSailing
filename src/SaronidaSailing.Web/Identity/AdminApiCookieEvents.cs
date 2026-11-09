using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace SaronidaSailing.Web.Identity;

public static class AdminApiCookieEvents
{
    private static readonly PathString ApiPrefix = new("/admin/api");

    public static Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context) =>
        RedirectOrStatus(context, StatusCodes.Status401Unauthorized);

    public static Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context) =>
        RedirectOrStatus(context, StatusCodes.Status403Forbidden);

    private static Task RedirectOrStatus(RedirectContext<CookieAuthenticationOptions> context, int apiStatus)
    {
        if (context.Request.Path.StartsWithSegments(ApiPrefix))
        {
            context.Response.StatusCode = apiStatus;
        }
        else
        {
            context.Response.Redirect(context.RedirectUri);
        }

        return Task.CompletedTask;
    }
}
