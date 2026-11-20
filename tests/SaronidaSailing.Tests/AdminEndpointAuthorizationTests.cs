using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SaronidaSailing.Web.Identity;

namespace SaronidaSailing.Tests;

[Collection(WebHostCollection.Name)]
public class AdminEndpointAuthorizationTests(TempDatabaseWebApplicationFactory factory) : IClassFixture<TempDatabaseWebApplicationFactory>
{
    private static readonly string[] AnonymousRoutes =
        ["/admin", "/admin/login", "/admin/login/code", "/admin/login/recovery", "/admin/instagram/callback"];

    private static readonly string[] AuthenticatedOnlyRoutes = ["/admin/logout", "/admin/two-factor/setup"];

    [Fact]
    public void EveryAdminEndpoint_DeclaresItsAuthorization()
    {
        var admin = AdminEndpoints();
        Assert.NotEmpty(admin);
        var offenders = new List<string>();

        foreach (var (route, endpoint) in admin)
        {
            var anonymous = endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;
            var authorize = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
            var policies = authorize.Select(a => a.Policy).ToList();

            var ok = anonymous
                ? AnonymousRoutes.Contains(route, StringComparer.OrdinalIgnoreCase)
                : AuthenticatedOnlyRoutes.Contains(route, StringComparer.OrdinalIgnoreCase)
                    ? authorize.Count > 0
                    : policies.Contains(AdminPolicy.Name);

            if (!ok)
            {
                offenders.Add(route);
            }
        }

        Assert.True(offenders.Count == 0, "Admin endpoints without the expected authorization: " + string.Join(", ", offenders.Distinct()));
    }

    [Fact]
    public void Allowlists_ListOnlyRoutesThatExist()
    {
        var routes = AdminEndpoints().Select(e => e.Route).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var stale = AnonymousRoutes.Concat(AuthenticatedOnlyRoutes).Where(r => !routes.Contains(r)).ToList();

        Assert.True(stale.Count == 0, "Allowlisted routes that no longer exist: " + string.Join(", ", stale));
    }

    private List<(string Route, Endpoint Endpoint)> AdminEndpoints() =>
        [.. factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => (Route: "/" + (e.RoutePattern.RawText ?? "").TrimStart('/'), Endpoint: (Endpoint)e))
            .Where(e => e.Route.StartsWith("/admin", StringComparison.OrdinalIgnoreCase)
                && (e.Route.Length == 6 || e.Route[6] == '/'))];
}
