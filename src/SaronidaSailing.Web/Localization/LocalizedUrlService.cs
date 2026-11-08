using Microsoft.AspNetCore.Localization;

namespace SaronidaSailing.Web.Localization;

public sealed class LocalizedUrlService(IHttpContextAccessor httpContextAccessor) : ILocalizedUrlService
{
    public string GetPath(string pageKey, string culture)
    {
        var page = SitePages.FindByKey(pageKey)
            ?? throw new ArgumentException($"Unknown page key '{pageKey}'.", nameof(pageKey));

        return culture switch
        {
            SiteCultures.Greek => "/" + page.GreekSlug,
            SiteCultures.English => $"/{SiteCultures.EnglishPrefix}/" + page.EnglishSlug,
            _ => throw new ArgumentException($"Unsupported culture '{culture}'.", nameof(culture)),
        };
    }

    public SitePage? GetCurrentPage()
    {
        var pagePath = httpContextAccessor.HttpContext?.Request.RouteValues["page"] as string;
        return pagePath is null ? null : SitePages.FindByPagePath(pagePath);
    }

    public string? GetAlternatePath()
    {
        var page = GetCurrentPage();
        if (page is null)
        {
            return null;
        }

        var current = httpContextAccessor.HttpContext!.Features.Get<IRequestCultureFeature>()
            ?.RequestCulture.UICulture.TwoLetterISOLanguageName;
        var other = current == SiteCultures.English ? SiteCultures.Greek : SiteCultures.English;
        return GetPath(page.Key, other);
    }
}
