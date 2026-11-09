using Microsoft.Extensions.Options;

namespace SaronidaSailing.Web;

public sealed class SiteOptions
{
    public const string SectionName = "Site";

    public string BaseUrl { get; set; } = "";

    public static string? NormalizeBaseUrl(string? value)
    {
        var trimmed = value?.Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(trimmed)
            || !Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || uri.AbsolutePath != "/"
            || uri.Query.Length > 0
            || uri.Fragment.Length > 0
            || uri.UserInfo.Length > 0)
        {
            return null;
        }

        return trimmed;
    }
}

public static class SiteOptionsExtensions
{
    public static IServiceCollection AddSiteOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SiteOptions>()
            .Configure(options =>
            {
                var raw = configuration[$"{SiteOptions.SectionName}:{nameof(SiteOptions.BaseUrl)}"];
                options.BaseUrl = SiteOptions.NormalizeBaseUrl(raw) ?? raw ?? "";
            })
            .Validate(
                options => SiteOptions.NormalizeBaseUrl(options.BaseUrl) is not null,
                "Site:BaseUrl must be an absolute http or https URL without a path, query or fragment.")
            .ValidateOnStart();

        return services;
    }
}
