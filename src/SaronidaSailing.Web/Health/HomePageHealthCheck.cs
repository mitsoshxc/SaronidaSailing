using System.Net;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SaronidaSailing.Web.Health;

public sealed class HomePageHealthCheck(IHttpClientFactory httpClientFactory, IConfiguration configuration) : IHealthCheck
{
    public const string HttpClientName = "health-home-page";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = configuration["Health:HomePageUrl"];
            if (string.IsNullOrWhiteSpace(url))
            {
                return HealthCheckResult.Unhealthy("Health:HomePageUrl is not configured.");
            }

            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return response.StatusCode == HttpStatusCode.OK
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy($"Home page returned {(int)response.StatusCode}.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("Home page check failed.", ex);
        }
    }
}
