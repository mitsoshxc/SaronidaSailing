namespace SaronidaSailing.Web.Health;

public static class HealthEndpointAccess
{
    private const string ForwardedFor = "X-Forwarded-For";
    private const string OriginalFor = "X-Original-For";

    public static bool IsProxied(HttpRequest request) =>
        request.Headers.ContainsKey(ForwardedFor) || request.Headers.ContainsKey(OriginalFor);

    public static IApplicationBuilder UseHealthDirectOnly(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase) && IsProxied(context.Request))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await next(context);
        });
}
