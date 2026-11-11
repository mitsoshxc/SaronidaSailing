namespace SaronidaSailing.Web.Endpoints;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/admin", ["GET", "HEAD"], (HttpContext http) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            return Results.Redirect("/admin/login");
        });

        return app;
    }
}
