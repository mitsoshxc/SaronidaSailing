using Microsoft.Extensions.Options;
using SaronidaSailing.Infrastructure.Instagram;
using SaronidaSailing.Web.Identity;

namespace SaronidaSailing.Web.Security;

public static class SecurityHeaders
{
    public const string ReferrerPolicy = "strict-origin-when-cross-origin";
    public const string ContentTypeOptions = "nosniff";

    public static string BuildContentSecurityPolicy(string instagramAuthorizeUrl, bool admin = false)
    {
        var instagramOrigin = new Uri(instagramAuthorizeUrl).GetLeftPart(UriPartial.Authority);
        var styleSrc = admin ? "'self' 'unsafe-inline'" : "'self'";
        var imgSrc = admin ? "'self' data:" : "'self'";
        return $"default-src 'self'; script-src 'self'; style-src {styleSrc}; img-src {imgSrc}; font-src 'self'; connect-src 'self'; "
            + "frame-src https://www.google.com; frame-ancestors 'self'; base-uri 'self'; "
            + $"form-action 'self' {instagramOrigin}; object-src 'none'";
    }

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
    {
        var options = app.ApplicationServices.GetRequiredService<IOptions<InstagramOptions>>().Value;
        var publicPolicy = BuildContentSecurityPolicy(options.AuthorizeUrl, admin: false);
        var adminPolicy = BuildContentSecurityPolicy(options.AuthorizeUrl, admin: true);

        return app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                var isAdmin = context.User.Identity?.IsAuthenticated == true
                    && context.User.HasClaim(AdminPolicy.MfaClaimType, AdminPolicy.MfaClaimValue);
                headers.ContentSecurityPolicy = isAdmin ? adminPolicy : publicPolicy;
                headers.XContentTypeOptions = ContentTypeOptions;
                headers["Referrer-Policy"] = ReferrerPolicy;
                return Task.CompletedTask;
            });

            return next(context);
        });
    }
}
