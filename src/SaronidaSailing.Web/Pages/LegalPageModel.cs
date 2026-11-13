using System.Globalization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SaronidaSailing.Core.Services;
using SaronidaSailing.Web.Content;
using SaronidaSailing.Web.Localization;
using SaronidaSailing.Web.Models;

namespace SaronidaSailing.Web.Pages;

public abstract class LegalPageModel(
    IPageContentAccessor accessor,
    ICmsEditContext editContext,
    ILocalizedUrlService urlService) : PageModel
{
    protected abstract string PageKey { get; }

    protected virtual string? SeeAlsoPageKey => null;

    public LegalPageViewModel Legal { get; private set; } = null!;

    public async Task OnGetAsync()
    {
        var locale = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == ContentLocales.English
            ? ContentLocales.English
            : ContentLocales.Greek;
        var toc = LegalPageViewModel.BuildToc(await accessor.GetAsync(), PageKey, await editContext.IsEditorAsync());
        var seeAlso = SeeAlsoPageKey is { } other
            ? new LegalSeeAlso(urlService.GetPath(other, locale), UiText.PageTitle(other).For(locale))
            : null;
        Legal = new LegalPageViewModel(PageKey, toc, seeAlso, urlService.GetPath("contact", locale));
    }
}
