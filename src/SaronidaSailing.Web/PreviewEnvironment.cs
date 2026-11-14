namespace SaronidaSailing.Web;

public static class PreviewEnvironment
{
    public const string Name = "Preview";

    public const string RobotsHeaderValue = "noindex, nofollow";

    public static bool IsPreview(this IHostEnvironment environment) => environment.IsEnvironment(Name);

    public static IApplicationBuilder UsePreviewNoIndex(this IApplicationBuilder app) => app.Use((context, next) =>
    {
        context.Response.OnStarting(static state =>
        {
            ((HttpResponse)state).Headers["X-Robots-Tag"] = RobotsHeaderValue;
            return Task.CompletedTask;
        }, context.Response);
        return next(context);
    });
}
