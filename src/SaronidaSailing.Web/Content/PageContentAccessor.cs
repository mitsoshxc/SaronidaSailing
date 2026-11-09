using System.Globalization;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Core.Services;
using SaronidaSailing.Web.Localization;

namespace SaronidaSailing.Web.Content;

public interface IPageContentAccessor
{
    Task<PageContent> GetAsync();
}

public sealed class PageContentAccessor(IContentService contentService, ILocalizedUrlService urlService)
    : IPageContentAccessor
{
    private Task<PageContent>? _content;

    public Task<PageContent> GetAsync() => _content ??= Load();

    private Task<PageContent> Load()
    {
        var pageKey = urlService.GetCurrentPage()?.Key ?? ContentKeys.Shared;
        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var locale = ContentLocales.IsSupported(language) ? language : ContentLocales.Default;
        return contentService.GetPageContentAsync(pageKey, locale);
    }
}
