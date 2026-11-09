using System.Globalization;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Web.Content;
using SaronidaSailing.Web.Localization;

namespace SaronidaSailing.Tests;

public sealed class PageContentAccessorTests
{
    [Fact]
    public async Task GetAsync_LoadsOncePerAccessor()
    {
        var service = new CountingContentService();
        var accessor = new PageContentAccessor(service, new FakeUrlService(null));

        var first = await accessor.GetAsync();
        var second = await accessor.GetAsync();

        Assert.Same(first, second);
        Assert.Single(service.Calls);
    }

    [Fact]
    public async Task GetAsync_UsesCurrentSitePageKey()
    {
        var service = new CountingContentService();
        var accessor = new PageContentAccessor(service, new FakeUrlService(SitePages.FindByKey("boat")));

        await accessor.GetAsync();

        Assert.Equal("boat", service.Calls[0].PageKey);
    }

    [Fact]
    public async Task GetAsync_OffSite_UsesShared()
    {
        var service = new CountingContentService();
        var accessor = new PageContentAccessor(service, new FakeUrlService(null));

        await accessor.GetAsync();

        Assert.Equal("shared", service.Calls[0].PageKey);
    }

    [Theory]
    [InlineData("en", "en")]
    [InlineData("el", "el")]
    [InlineData("de", "el")]
    public async Task GetAsync_MapsUiCultureToLocale(string culture, string expected)
    {
        var service = new CountingContentService();
        var accessor = new PageContentAccessor(service, new FakeUrlService(null));
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);
            await accessor.GetAsync();
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }

        Assert.Equal(expected, service.Calls[0].Locale);
    }

    private sealed class FakeUrlService(SitePage? current) : ILocalizedUrlService
    {
        public string GetPath(string pageKey, string culture) => throw new NotSupportedException();

        public SitePage? GetCurrentPage() => current;

        public string? GetAlternatePath() => null;
    }

    private sealed class CountingContentService : IContentService
    {
        public List<(string PageKey, string Locale)> Calls { get; } = [];

        public Task<PageContent> GetPageContentAsync(string pageKey, string locale, CancellationToken ct = default)
        {
            Calls.Add((pageKey, locale));
            return Task.FromResult(new PageContent(
                pageKey,
                locale,
                new Dictionary<string, ContentValue>(),
                new Dictionary<string, IReadOnlyList<ContentListEntry>>(),
                new Dictionary<string, bool>()));
        }

        public Task<ContentSaveResult> ApplyChangesAsync(
            IReadOnlyList<ContentChange> changes, ContentEditor editor, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<FieldVersion>> GetFieldHistoryAsync(
            string key, string locale, Guid? listItemId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ContentSaveDetails?> GetSaveAsync(Guid saveId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
