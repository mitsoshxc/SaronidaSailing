using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SaronidaSailing.Core.Interfaces;

namespace SaronidaSailing.Infrastructure.Instagram;

public static class InstagramServiceCollectionExtensions
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    public static IHttpClientBuilder AddInstagram(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ISecretProtector, DataProtectionSecretProtector>();

        services.AddOptions<InstagramOptions>()
            .Bind(configuration.GetSection(InstagramOptions.SectionName))
            .Validate(o => IsAbsoluteHttpUrl(o.AuthorizeUrl), "Instagram:AuthorizeUrl must be an absolute http(s) URL.")
            .Validate(o => IsAbsoluteHttpUrl(o.ApiBaseUrl), "Instagram:ApiBaseUrl must be an absolute http(s) URL.")
            .Validate(o => IsAbsoluteHttpUrl(o.GraphBaseUrl), "Instagram:GraphBaseUrl must be an absolute http(s) URL.")
            .ValidateOnStart();

        return services.AddHttpClient<IInstagramApiClient, InstagramApiClient>(client => client.Timeout = RequestTimeout);
    }

    private static bool IsAbsoluteHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
}
