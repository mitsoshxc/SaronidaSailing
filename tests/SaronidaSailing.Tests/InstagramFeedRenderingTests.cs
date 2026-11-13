using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Core.Services;
using SaronidaSailing.Infrastructure.Data;
using static SaronidaSailing.Tests.AdminTestHelpers;

namespace SaronidaSailing.Tests;

[Collection(WebHostCollection.Name)]
public class InstagramFeedRenderingTests(TempDatabaseWebApplicationFactory factory)
    : IClassFixture<TempDatabaseWebApplicationFactory>, IAsyncLifetime
{
    private static readonly DateTime PostedAt = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    public Task InitializeAsync() => ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task ResetAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Set<InstagramPost>().RemoveRange(await db.Set<InstagramPost>().ToListAsync());
        db.Set<MediaAsset>().RemoveRange(await db.Set<MediaAsset>().Where(a => a.Source == MediaSource.Instagram).ToListAsync());
        db.Set<ContentItem>().RemoveRange(
            await db.Set<ContentItem>().Where(i => i.PageKey == "home" && i.SectionKey == "instagram").ToListAsync());
        db.Set<ContentSection>().RemoveRange(
            await db.Set<ContentSection>().Where(s => s.PageKey == "home" && s.SectionKey == "instagram").ToListAsync());
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<IPageCacheInvalidator>().EvictPagesAsync(["home"]);
    }

    private static MediaAsset NewAsset(string name, IReadOnlyCollection<int> widths, int size) =>
        MediaAsset.Create(
            $"{name}.jpg", Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"), name, widths, size, size, ".jpg",
            source: MediaSource.Instagram);

    private async Task SeedPostsAsync(int count, Func<int, string?>? caption = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        for (var i = 1; i <= count; i++)
        {
            var asset = NewAsset($"instagram-{i}", [480, 960], 960);
            db.Set<MediaAsset>().Add(asset);
            db.Set<InstagramPost>().Add(InstagramPost.Create(
                $"post-{i}", $"https://www.instagram.com/p/post-{i}/", caption is null ? $"Caption {i}" : caption(i), "IMAGE",
                asset.Id, PostedAt.AddMinutes(-i)));
        }

        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<IPageCacheInvalidator>().EvictPagesAsync(["home"]);
    }

    private async Task WriteAsync(params ContentChange[] changes)
    {
        using var scope = factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IContentService>()
            .ApplyChangesAsync(changes, new ContentEditor("feed-test@example.com", null));
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
    }

    private Task SetCountAsync(string count) =>
        WriteAsync(new SetField(InstagramSection.PostCountKey, "el", ContentType.Text, count));

    private async Task<HttpClient> NewAdminClientAsync(string email)
    {
        var client = NewClient(factory);
        await LoginWithTotpAsync(client, await AddTwoFactorAdminAsync(factory, email));
        return client;
    }

    private static int Tiles(string html) => Regex.Matches(html, @"<li[^>]*><a href=""https://www\.instagram\.com/").Count;

    private static int HiddenTiles(string html) => Regex.Matches(html, @"<li hidden=""hidden""><a ").Count;

    [Fact]
    public async Task Visitor_WithNoCount_SeesSixTiles()
    {
        await SeedPostsAsync(8);

        var html = await NewClient(factory).GetStringAsync("/");

        Assert.Equal(6, Tiles(html));
        Assert.Equal(0, HiddenTiles(html));
    }

    [Fact]
    public async Task Visitor_WithCountThree_SeesThreeNewestTiles()
    {
        await SeedPostsAsync(8);
        await SetCountAsync("3");

        var html = await NewClient(factory).GetStringAsync("/");

        Assert.Equal(3, Tiles(html));
        Assert.Contains("/p/post-1/", html);
        Assert.Contains("/p/post-3/", html);
        Assert.DoesNotContain("/p/post-4/", html);
    }

    [Fact]
    public async Task Visitor_WithClearedCount_FallsBackToSix()
    {
        await SeedPostsAsync(8);
        await SetCountAsync("3");
        await SetCountAsync("");

        Assert.Equal(6, Tiles(await NewClient(factory).GetStringAsync("/")));
    }

    [Fact]
    public async Task Visitor_WithFewerPostsThanCount_SeesAllOfThem()
    {
        await SeedPostsAsync(2);
        await SetCountAsync("9");

        Assert.Equal(2, Tiles(await NewClient(factory).GetStringAsync("/")));
    }

    [Fact]
    public async Task GreekCount_AppliesToTheEnglishPageToo()
    {
        await SeedPostsAsync(8);
        await SetCountAsync("4");

        Assert.Equal(4, Tiles(await NewClient(factory).GetStringAsync("/en/")));
    }

    [Fact]
    public async Task Visitor_WithNoPosts_GetsNoSection()
    {
        await WriteAsync(new SetField(InstagramSection.TitleKey, "el", ContentType.Text, "Instagram"));
        var client = NewClient(factory);

        foreach (var url in new[] { "/", "/en/" })
        {
            var html = await client.GetStringAsync(url);

            Assert.DoesNotContain("instagram-feed", html);
            Assert.DoesNotContain("data-cms", html);
        }
    }

    [Fact]
    public async Task InactiveSection_RendersNothingForVisitors()
    {
        await SeedPostsAsync(3);
        await WriteAsync(new SetSectionActive(InstagramSection.Key, false));

        var html = await NewClient(factory).GetStringAsync("/");

        Assert.DoesNotContain("instagram-feed", html);
        Assert.DoesNotContain("/p/post-1/", html);
    }

    [Fact]
    public async Task Tiles_LinkToThePost_AndCarryResponsiveImageAttributes()
    {
        await SeedPostsAsync(1);
        await WriteAsync(new SetField(InstagramSection.TitleKey, "el", ContentType.Text, "Στο Instagram"));
        string slug;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            slug = (await db.Set<MediaAsset>().SingleAsync(a => a.Source == MediaSource.Instagram)).Slug;
        }

        var html = await NewClient(factory).GetStringAsync("/");

        Assert.Contains("<section class=\"instagram-feed\" aria-labelledby=\"instagram-feed-title\">", html);
        Assert.Contains("<h2 id=\"instagram-feed-title\">Στο Instagram</h2>", html);
        Assert.Contains("<ul class=\"instagram-feed-grid\">", html);
        Assert.Contains("<a href=\"https://www.instagram.com/p/post-1/\">", html);
        var img = Regex.Match(html, @"<img [^>]*>").Value;
        Assert.Matches($@"src=""/media/{slug}-[0-9a-f]{{16}}-480\.webp""", img);
        Assert.Matches(
            $@"srcset=""/media/{slug}-[0-9a-f]{{16}}-480\.webp 480w, /media/{slug}-[0-9a-f]{{16}}-960\.webp 960w""", img);
        Assert.Contains("sizes=\"(min-width: 960px) 25vw, (min-width: 720px) 33vw, 50vw\"", img);
        Assert.Contains("width=\"960\"", img);
        Assert.Contains("height=\"960\"", img);
        Assert.Contains("alt=\"Caption 1\"", img);
        Assert.Contains("loading=\"lazy\"", img);
        Assert.Contains("decoding=\"async\"", img);
        Assert.DoesNotContain("target=", html);
    }

    [Fact]
    public async Task Alt_UsesTheFirstCaptionLine()
    {
        await SeedPostsAsync(1, _ => "  Sunset   at sea\nsecond line #tag");

        var html = await NewClient(factory).GetStringAsync("/");

        Assert.Contains("alt=\"Sunset at sea\"", html);
    }

    [Fact]
    public async Task Alt_WithoutCaption_IsLocalizedWithTheDate()
    {
        await SeedPostsAsync(1, _ => null);
        var client = NewClient(factory);

        var english = await client.GetStringAsync("/en/");
        var greek = await client.GetStringAsync("/");

        Assert.Contains("alt=\"Instagram post, 1 November 2026\"", english);
        Assert.Contains("alt=\"Ανάρτηση στο Instagram, 1 Νοεμβρίου 2026\"", greek);
    }

    [Fact]
    public async Task Admin_WithCountThree_GetsAllPosts_HiddenBeyondTheCount_AndDataAttributes()
    {
        await SeedPostsAsync(8);
        await SetCountAsync("3");
        var admin = await NewAdminClientAsync("feed-admin@example.com");

        var response = await admin.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal(8, Tiles(html));
        Assert.Equal(5, HiddenTiles(html));
        Assert.Matches(@"<li><a href=""https://www\.instagram\.com/p/post-3/"">", html);
        Assert.Matches(@"<li hidden=""hidden""><a href=""https://www\.instagram\.com/p/post-4/"">", html);
        Assert.Contains("data-cms-instagram-count=\"3\"", html);
        Assert.Contains("data-cms-instagram-count-key=\"home.instagram.post-count\"", html);
        Assert.Contains("data-cms-section=\"home.instagram\"", html);
        Assert.Contains("data-cms-section-label=\"Instagram feed\"", html);
        Assert.DoesNotContain("data-cms-admin-note", html);
        Assert.DoesNotContain("data-cms-empty", Regex.Match(html, @"<section[^>]*home.instagram[^>]*>").Value);
    }

    [Fact]
    public async Task Admin_WithoutCount_ShowsTheDefaultSix()
    {
        await SeedPostsAsync(8);
        var admin = await NewAdminClientAsync("feed-admin-default@example.com");

        var html = await admin.GetStringAsync("/en/");

        Assert.Equal(8, Tiles(html));
        Assert.Equal(2, HiddenTiles(html));
        Assert.Contains("data-cms-instagram-count=\"6\"", html);
    }

    [Fact]
    public async Task Admin_WithNoPosts_SeesTheSectionNoteAndSettingsLink()
    {
        var admin = await NewAdminClientAsync("feed-admin-empty@example.com");

        var html = await admin.GetStringAsync("/");

        Assert.Contains("data-cms-section=\"home.instagram\"", html);
        Assert.Contains("data-cms-instagram-count=\"6\"", html);
        Assert.Contains("data-cms-empty", Regex.Match(html, @"<section[^>]*home.instagram[^>]*>").Value);
        Assert.Contains("No Instagram posts yet.", html);
        Assert.Contains("<a href=\"/admin/instagram\">Instagram settings</a>", html);
        Assert.Equal(0, Tiles(html));
        Assert.DoesNotContain("<ul class=\"instagram-feed-grid\">", html);
    }

    [Fact]
    public async Task Admin_SeesAnInactiveSection_Dimmed()
    {
        await SeedPostsAsync(2);
        await WriteAsync(new SetSectionActive(InstagramSection.Key, false));
        var admin = await NewAdminClientAsync("feed-admin-inactive@example.com");

        var html = await admin.GetStringAsync("/");

        Assert.Contains("data-cms-active=\"false\"", html);
        Assert.Equal(2, Tiles(html));
    }

    [Fact]
    public async Task Visitor_Html_HasNoAdminMarkup()
    {
        await SeedPostsAsync(8);
        await SetCountAsync("5");
        var client = NewClient(factory);

        foreach (var url in new[] { "/", "/en/" })
        {
            var html = await client.GetStringAsync(url);

            Assert.DoesNotContain("data-cms", html);
            Assert.DoesNotMatch(@"\shidden=", html);
            Assert.DoesNotContain("/admin/instagram", html);
        }
    }

    [Fact]
    public async Task Eviction_OfHome_MakesNewPostsAppear()
    {
        await SeedPostsAsync(2);
        var client = NewClient(factory);
        Assert.Equal(2, Tiles(await client.GetStringAsync("/")));

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var asset = NewAsset("instagram-new", [480], 480);
            db.Set<MediaAsset>().Add(asset);
            db.Set<InstagramPost>().Add(InstagramPost.Create(
                "post-new", "https://www.instagram.com/p/post-new/", "New one", "IMAGE", asset.Id, PostedAt.AddHours(1)));
            await db.SaveChangesAsync();
        }

        Assert.Equal(2, Tiles(await client.GetStringAsync("/")));

        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPageCacheInvalidator>().EvictPagesAsync(["home"]);
        }

        var html = await client.GetStringAsync("/");
        Assert.Equal(3, Tiles(html));
        Assert.Contains("/p/post-new/", html);
    }
}
