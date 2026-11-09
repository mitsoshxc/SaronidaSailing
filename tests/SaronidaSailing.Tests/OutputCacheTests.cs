using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Web.Caching;
using SaronidaSailing.Web.Localization;

namespace SaronidaSailing.Tests;

public class CmsPageCachePolicyTests
{
    public static TheoryData<string> BuiltPageKeys()
    {
        var data = new TheoryData<string>();
        foreach (var page in SitePages.All.Where(p => FindPageModel(p) is not null))
        {
            data.Add(page.Key);
        }

        return data;
    }

    private static Type? FindPageModel(SitePage page)
    {
        var name = $"SaronidaSailing.Web.Pages.{page.PagePath.TrimStart('/').Replace('/', '.')}Model";
        return typeof(CacheTags).Assembly.GetType(name);
    }

    [Fact]
    public void AtLeastOneSitePageIsBuilt() => Assert.NotEmpty(BuiltPageKeys());

    [Theory]
    [MemberData(nameof(BuiltPageKeys))]
    public void SitePageModel_UsesCmsPagePolicy_WithItsPageTag(string key)
    {
        var page = SitePages.FindByKey(key)!;
        var model = FindPageModel(page)!;

        var attribute = model.GetCustomAttribute<OutputCacheAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal("CmsPage", attribute.PolicyName);
        Assert.Equal([CacheTags.Page(page.Key)], attribute.Tags!);
    }

    [Fact]
    public void HomePageConstant_MatchesBuilder() => Assert.Equal(CacheTags.HomePage, CacheTags.Page("home"));
}

[Collection(WebHostCollection.Name)]
public class OutputCacheWebHostTests : IClassFixture<TempDatabaseWebApplicationFactory>
{
    private readonly TempDatabaseWebApplicationFactory _factory;

    public OutputCacheWebHostTests(TempDatabaseWebApplicationFactory factory) => _factory = factory;

    private static string TitleKey(string page) => $"{page}.seo.title";

    private async Task WriteThroughService(string key, string value)
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IContentService>();
        var result = await service.ApplyChangesAsync(
            [new SetField(key, "el", ContentType.Text, value)], new ContentEditor("test@example.com", null));
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
    }

    private async Task WriteBypassingService(string pageKey, string value)
    {
        using var scope = _factory.Services.CreateScope();
        var items = scope.ServiceProvider.GetRequiredService<IContentItemRepository>();
        var item = await items.FindStandaloneAsync(pageKey, "seo", "title", "el");
        Assert.NotNull(item);
        item.SetValue(value, "bypass@example.com");
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    private HttpClient NewClient() => AdminTestHelpers.NewClient(_factory);

    private static Task<string> Get(HttpClient client, string url) => client.GetStringAsync(url);

    [Fact]
    public async Task Home_IsServedFromCache_UntilServiceWriteEvicts()
    {
        var client = NewClient();
        await WriteThroughService(TitleKey("home"), "Title A");
        Assert.Contains("<title>Title A</title>", await Get(client, "/"));

        await WriteBypassingService("home", "Title B");
        Assert.Contains("<title>Title A</title>", await Get(client, "/"));

        await WriteThroughService(TitleKey("home"), "Title C");
        Assert.Contains("<title>Title C</title>", await Get(client, "/"));
    }

    [Fact]
    public async Task SharedWrite_EvictsEveryPage()
    {
        var client = NewClient();
        await WriteThroughService(TitleKey("home"), "Greek A");
        Assert.Contains("<title>Greek A</title>", await Get(client, "/"));
        Assert.Contains("<title>Greek A</title>", await Get(client, "/en/"));

        await WriteBypassingService("home", "Greek B");
        Assert.Contains("<title>Greek A</title>", await Get(client, "/"));
        Assert.Contains("<title>Greek A</title>", await Get(client, "/en/"));

        await WriteThroughService("shared.footer.copyright", "anything");
        Assert.Contains("<title>Greek B</title>", await Get(client, "/"));
        Assert.Contains("<title>Greek B</title>", await Get(client, "/en/"));
    }

    [Fact]
    public async Task GreekAndEnglish_AreSeparateEntries()
    {
        var client = NewClient();
        await WriteThroughService(TitleKey("home"), "Sep A");
        Assert.Contains("og:locale\" content=\"el_GR\"", await Get(client, "/"));

        await WriteBypassingService("home", "Sep B");

        var english = await Get(client, "/en/");
        Assert.Contains("<title>Sep B</title>", english);
        Assert.Contains("og:locale\" content=\"en_GB\"", english);
        Assert.Contains("<title>Sep A</title>", await Get(client, "/"));
    }

    [Fact]
    public async Task QueryString_IsIgnored()
    {
        var client = NewClient();
        await WriteThroughService(TitleKey("home"), "Query A");
        Assert.Contains("<title>Query A</title>", await Get(client, "/"));

        await WriteBypassingService("home", "Query B");

        Assert.Contains("<title>Query A</title>", await Get(client, "/?x=1"));
        Assert.Contains("<title>Query A</title>", await Get(client, "/?x=2"));
    }

    [Fact]
    public async Task QueryString_PrimedFirst_IsReusedByPlainUrl()
    {
        var client = NewClient();
        await WriteThroughService(TitleKey("home"), "Qfirst A");
        Assert.Contains("<title>Qfirst A</title>", await Get(client, "/?x=1"));

        await WriteBypassingService("home", "Qfirst B");

        Assert.Contains("<title>Qfirst A</title>", await Get(client, "/"));
        Assert.Contains("<title>Qfirst A</title>", await Get(client, "/?y=2"));
    }

    [Fact]
    public async Task Admin_IsNeverServedFromOrStoredInCache()
    {
        await WriteThroughService(TitleKey("home"), "Admin A");
        var anonymous = NewClient();
        Assert.Contains("<title>Admin A</title>", await Get(anonymous, "/"));

        await WriteBypassingService("home", "Admin B");

        var admin = NewClient();
        var twoFactorAdmin = await AdminTestHelpers.AddTwoFactorAdminAsync(_factory, "cache-admin@example.com");
        await AdminTestHelpers.LoginWithTotpAsync(admin, twoFactorAdmin);
        Assert.Contains("<title>Admin B</title>", await Get(admin, "/"));

        Assert.Contains("<title>Admin A</title>", await Get(anonymous, "/"));
    }

    [Fact]
    public async Task ErrorPage_IsNotCached()
    {
        var client = NewClient();

        var first = await client.GetAsync("/Error");
        var second = await client.GetAsync("/Error");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Null(first.Headers.Age);
        Assert.Null(second.Headers.Age);
        Assert.NotEqual(
            await first.Content.ReadAsStringAsync().ContinueWith(t => ExtractRequestId(t.Result)),
            await second.Content.ReadAsStringAsync().ContinueWith(t => ExtractRequestId(t.Result)));
    }

    private static string ExtractRequestId(string html) =>
        System.Text.RegularExpressions.Regex.Match(html, "<code>([^<]*)</code>").Groups[1].Value;
}
