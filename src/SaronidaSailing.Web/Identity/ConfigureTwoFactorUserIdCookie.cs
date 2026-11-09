using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace SaronidaSailing.Web.Identity;

public sealed class ConfigureTwoFactorUserIdCookie : IConfigureNamedOptions<CookieAuthenticationOptions>
{
    public void Configure(string? name, CookieAuthenticationOptions options)
    {
        if (name != IdentityConstants.TwoFactorUserIdScheme)
        {
            return;
        }

        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
    }

    public void Configure(CookieAuthenticationOptions options) => Configure(Options.DefaultName, options);
}
