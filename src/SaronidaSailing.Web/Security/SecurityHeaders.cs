using Microsoft.Extensions.Options;
using SaronidaSailing.Infrastructure.Instagram;

namespace SaronidaSailing.Web.Security;

public static class SecurityHeaders
{
    public const string ReferrerPolicy = "strict-origin-when-cross-origin";
    public const string ContentTypeOptions = "nosniff";

    public static string BuildContentSecurityPolicy(string instagramAuthorizeUrl)
    {
        var instagramOrigin = new Uri(instagramAuthorizeUrl).GetLeftPart(UriPartial.Authority);
        return "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self'; font-src 'self'; connect-src 'self'; "
            + "frame-src https://www.google.com; frame-ancestors 'self'; base-uri 'self'; "
            + $"form-action 'self' {instagramOrigin}; object-src 'none'";
    }

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
    {
        var options = app.ApplicationServices.GetRequiredService<IOptions<InstagramOptions>>().Value;
        var contentSecurityPolicy = BuildContentSecurityPolicy(options.AuthorizeUrl);

        return app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.ContentSecurityPolicy = contentSecurityPolicy;
                headers.XContentTypeOptions = ContentTypeOptions;
                headers["Referrer-Policy"] = ReferrerPolicy;
                return Task.CompletedTask;
            });

            return next(context);
        });
    }
}
