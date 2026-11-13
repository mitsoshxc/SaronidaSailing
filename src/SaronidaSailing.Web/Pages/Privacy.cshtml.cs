using Microsoft.AspNetCore.OutputCaching;
using SaronidaSailing.Web.Caching;
using SaronidaSailing.Web.Content;
using SaronidaSailing.Web.Localization;

namespace SaronidaSailing.Web.Pages;

[OutputCache(PolicyName = CacheTags.PolicyName, Tags = [CacheTags.PrivacyPage])]
public class PrivacyModel(IPageContentAccessor accessor, ICmsEditContext editContext, ILocalizedUrlService urlService)
    : LegalPageModel(accessor, editContext, urlService)
{
    protected override string PageKey => "privacy";

    protected override string? SeeAlsoPageKey => "terms";
}
