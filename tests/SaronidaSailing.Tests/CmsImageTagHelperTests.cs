using System.Net;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Web.Content;
using SaronidaSailing.Web.TagHelpers;
using static SaronidaSailing.Tests.AdminTestHelpers;
using static SaronidaSailing.Tests.TagHelperTestSupport;

namespace SaronidaSailing.Tests;

public sealed class CmsImageTagHelperTests
{
    private static readonly Guid Id = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private const string Srcset =
        "/media/photo-480.webp 480w, /media/photo-960.webp 960w, /media/photo-1600.webp 1600w, /media/photo-2400.webp 2400w";

    private static PageContent Standalone(
        string locale = "el",
        Dictionary<string, ContentValue>? fields = null,
        PageImage? photo = null,
        bool withPhoto = true)
    {
        var all = new Dictionary<string, ContentValue> { ["home.hero.image"] = Image(Id, locale == "en") };
        foreach (var (key, value) in fields ?? [])
        {
            all[key] = value;
        }

        return Content(
            locale,
            all,
            images: withPhoto ? new Dictionary<Guid, PageImage> { [Id] = photo ?? Photo(Id) } : []);
    }

    private static async Task<string> Run(
        PageContent content,
        ICmsEditContext edit,
        Action<CmsImageTagHelper>? configure = null,
        ILogger<CmsImageTagHelper>? logger = null,
        params (string Name, string Value)[] extraAttributes)
    {
        var attributes = new TagHelperAttributeList { { "class", "hero" } };
        foreach (var (name, value) in extraAttributes)
        {
            attributes.Add(name, value);
        }

        var helper = new CmsImageTagHelper(
            new FakePageContentAccessor(content), edit, logger ?? new ListLogger<CmsImageTagHelper>())
        {
            Key = "home.hero.image",
            Sizes = "100vw",
        };
        configure?.Invoke(helper);

        var output = NewOutput("cms-image", attributes);
        await helper.ProcessAsync(NewContext("cms-image", attributes), output);
        return Render(output);
    }

    [Fact]
    public async Task Visitor_RendersFullImageTag()
    {
        var content = Standalone(fields: new() { ["home.hero.image-alt"] = Text("A boat") });

        var html = await Run(content, Visitor);

        Assert.Equal(
            $"<img class=\"hero\" src=\"/media/photo-960.webp\" srcset=\"{Srcset}\" sizes=\"100vw\""
            + " width=\"3000\" height=\"2000\" alt=\"A boat\" loading=\"lazy\" decoding=\"async\">",
            html);
    }

    [Fact]
    public async Task Visitor_EagerLoading_AndAttributesPassThrough()
    {
        var content = Standalone(fields: new() { ["home.hero.image-alt"] = Text("A boat") });

        var html = await Run(
            content, Visitor, h => h.Loading = "eager", extraAttributes: [("fetchpriority", "high"), ("id", "hero-img")]);

        Assert.Contains("class=\"hero\"", html);
        Assert.Contains("fetchpriority=\"high\"", html);
        Assert.Contains("id=\"hero-img\"", html);
        Assert.Contains("loading=\"eager\"", html);
    }

    [Theory]
    [InlineData(new[] { 480, 960 }, "/media/photo-960.webp")]
    [InlineData(new[] { 480, 700 }, "/media/photo-700.webp")]
    [InlineData(new[] { 320 }, "/media/photo-320.webp")]
    public async Task Src_IsThe960Source_OrTheLargest(int[] widths, string expected)
    {
        var content = Standalone(photo: Photo(Id, 3000, 2000, widths));

        var html = await Run(content, Visitor);

        Assert.Contains($" src=\"{expected}\"", html);
    }

    [Fact]
    public async Task Alt_IsHtmlEncoded()
    {
        var content = Standalone(fields: new() { ["home.hero.image-alt"] = Text("A \"big\" <b>boat</b> & more") });

        var html = await Run(content, Visitor);

        Assert.Contains("alt=\"A &quot;big&quot; &lt;b&gt;boat&lt;/b&gt; &amp; more\"", html);
        Assert.DoesNotContain("<b>", html);
    }

    [Fact]
    public async Task MissingAlt_RendersEmptyAlt()
    {
        var html = await Run(Standalone(), Visitor);

        Assert.Contains(" alt=\"\"", html);
    }

    [Fact]
    public async Task English_UsesGreekImage_AndEnglishAlt()
    {
        var content = Standalone("en", new() { ["home.hero.image-alt"] = Text("English alt") });

        var html = await Run(content, Visitor);

        Assert.Contains("src=\"/media/photo-960.webp\"", html);
        Assert.Contains("alt=\"English alt\"", html);
    }

    [Fact]
    public async Task English_FallsBackToGreekAlt()
    {
        var content = Standalone("en", new() { ["home.hero.image-alt"] = new ContentValue("Ελληνικό", ContentType.Text, true) });

        var html = await Run(content, Visitor);

        Assert.Contains("alt=\"Ελληνικό\"", html);
        Assert.DoesNotContain("data-cms", html);
    }

    [Fact]
    public async Task Visitor_UnknownAssetOrBadId_RendersNothing_AndWarns()
    {
        var logger = new ListLogger<CmsImageTagHelper>();
        var missingAsset = Standalone(withPhoto: false);
        var notAGuid = Standalone(fields: new() { ["home.hero.image"] = new ContentValue("nope", ContentType.Image, false) });

        Assert.Equal("", await Run(missingAsset, Visitor, logger: logger));
        Assert.Equal("", await Run(notAGuid, Visitor, logger: logger));

        Assert.Equal(2, logger.Warnings.Count);
        Assert.Contains("home.hero.image", logger.Warnings[0]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Visitor_EmptyValue_RendersNothing_WithoutWarning(string value)
    {
        var logger = new ListLogger<CmsImageTagHelper>();
        var content = Standalone(fields: new() { ["home.hero.image"] = new ContentValue(value, ContentType.Image, false) });

        Assert.Equal("", await Run(content, Visitor, logger: logger));
        Assert.Empty(logger.Warnings);
    }

    [Fact]
    public async Task Visitor_NoField_RendersNothing()
    {
        var logger = new ListLogger<CmsImageTagHelper>();

        Assert.Equal("", await Run(Content(), Visitor, logger: logger));
        Assert.Empty(logger.Warnings);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WrongType_RendersNothing_AndWarns(bool editor)
    {
        var logger = new ListLogger<CmsImageTagHelper>();
        var content = Standalone(fields: new() { ["home.hero.image"] = Text("x") });

        Assert.Equal("", await Run(content, editor ? Editor : Visitor, logger: logger));
        Assert.Single(logger.Warnings);
    }

    [Fact]
    public async Task Editor_HasAllDataAttributes()
    {
        var content = Standalone(fields: new()
        {
            ["home.hero.image-alt"] = Text("A boat"),
            ["home.hero.title"] = Text("Sail with us"),
        });

        var html = await Run(content, Editor, h => h.AltSource = "home.hero.title");

        Assert.Equal(
            $"<img class=\"hero\" src=\"/media/photo-960.webp\" srcset=\"{Srcset}\" sizes=\"100vw\""
            + " width=\"3000\" height=\"2000\" alt=\"A boat\" loading=\"lazy\" decoding=\"async\""
            + " data-cms-key=\"home.hero.image\" data-cms-type=\"image\""
            + $" data-cms-value=\"{Id}\" data-cms-alt-key=\"home.hero.image-alt\" data-cms-alt=\"A boat\""
            + " data-cms-alt-el=\"A boat\" data-cms-alt-proposal=\"Sail with us\">",
            html);
    }

    [Fact]
    public async Task Editor_English_FallbackAlt_IsFlagged_AndCurrentAltIsEmpty()
    {
        var content = Standalone("en", new() { ["home.hero.image-alt"] = new ContentValue("Ελληνικό", ContentType.Text, true) });

        var html = await Run(content, Editor);

        Assert.Contains("alt=\"Ελληνικό\"", html);
        Assert.Contains(" data-cms-alt=\"\"", html);
        Assert.Contains(" data-cms-alt-el=\"Ελληνικό\"", html);
        Assert.Contains(" data-cms-fallback", html);
    }

    [Fact]
    public async Task Editor_English_OwnAlt_IsNotFlagged_AndGreekAltIsUnknown()
    {
        var content = Standalone("en", new() { ["home.hero.image-alt"] = Text("English alt") });

        var html = await Run(content, Editor);

        Assert.Contains(" data-cms-alt=\"English alt\"", html);
        Assert.DoesNotContain("data-cms-fallback", html);
        Assert.DoesNotContain("data-cms-alt-el", html);
    }

    [Fact]
    public async Task Editor_NoAlt_HasEmptyAltAttributes_AndNoFallbackFlag()
    {
        var html = await Run(Standalone("en"), Editor);

        Assert.Contains(" data-cms-alt=\"\"", html);
        Assert.Contains(" data-cms-alt-el=\"\"", html);
        Assert.DoesNotContain("data-cms-fallback", html);
        Assert.DoesNotContain("data-cms-alt-proposal", html);
    }

    [Fact]
    public async Task Editor_AltProposal_StripsRichText_AndFallsBack()
    {
        var content = Standalone(fields: new()
        {
            ["home.hero.body"] = Rich("<p>Hello <strong>sea</strong> &amp; sky</p><ul><li>One</li><li>Two</li></ul>"),
        });

        var html = await Run(content, Editor, h => h.AltSource = "home.hero.body");

        Assert.Contains(" data-cms-alt-proposal=\"Hello sea &amp; sky One Two\"", html);
    }

    [Fact]
    public async Task Editor_AltProposal_UsesGreekFallbackOnEnglishPage_AndEmptyWhenMissing()
    {
        var content = Standalone("en", new()
        {
            ["home.hero.title"] = new ContentValue("Τίτλος", ContentType.Text, true),
        });

        Assert.Contains(
            " data-cms-alt-proposal=\"Τίτλος\"", await Run(content, Editor, h => h.AltSource = "home.hero.title"));
        Assert.Contains(
            " data-cms-alt-proposal=\"\"", await Run(content, Editor, h => h.AltSource = "home.hero.nothing"));
    }

    [Fact]
    public async Task Visitor_AltSource_IsNotRendered()
    {
        var content = Standalone(fields: new() { ["home.hero.title"] = Text("T") });

        var html = await Run(content, Visitor, h => h.AltSource = "home.hero.title");

        Assert.DoesNotContain("data-cms", html);
        Assert.DoesNotContain("alt-source", html);
    }

    [Fact]
    public async Task Editor_NoImage_RendersPlaceholderButton()
    {
        var content = Content(fields: new() { ["home.hero.title"] = Text("T") });

        var html = await Run(content, Editor, h => h.AltSource = "home.hero.title", extraAttributes: ("fetchpriority", "high"));

        Assert.Equal(
            "<button type=\"button\" class=\"hero cms-image-placeholder\" data-cms-key=\"home.hero.image\""
            + " data-cms-type=\"image\" data-cms-value=\"\" data-cms-alt-key=\"home.hero.image-alt\""
            + " data-cms-alt=\"\" data-cms-alt-el=\"\" data-cms-alt-proposal=\"T\" data-cms-empty>Add image</button>",
            html);
    }

    [Fact]
    public async Task Editor_PlaceholderWithoutLayoutClass_HasOnlyPlaceholderClass()
    {
        var attributes = new TagHelperAttributeList();
        var helper = new CmsImageTagHelper(
            new FakePageContentAccessor(Content()), Editor, new ListLogger<CmsImageTagHelper>())
        {
            Key = "home.hero.image",
            Sizes = "100vw",
        };
        var output = NewOutput("cms-image", attributes);

        await helper.ProcessAsync(NewContext("cms-image", attributes), output);

        Assert.StartsWith("<button type=\"button\" class=\"cms-image-placeholder\"", Render(output));
    }

    [Fact]
    public async Task Editor_MissingAsset_RendersPlaceholder_AndWarns()
    {
        var logger = new ListLogger<CmsImageTagHelper>();

        var html = await Run(Standalone(withPhoto: false), Editor, logger: logger);

        Assert.StartsWith("<button", html);
        Assert.Contains("data-cms-value=\"\"", html);
        Assert.Contains("data-cms-empty", html);
        Assert.Single(logger.Warnings);
    }

    [Fact]
    public async Task Editor_English_PlaceholderWithFallbackAlt_IsFlagged()
    {
        var content = Content("en", new() { ["home.hero.image-alt"] = new ContentValue("Alt", ContentType.Text, true) });

        var html = await Run(content, Editor);

        Assert.Contains(" data-cms-fallback", html);
    }

    private static async Task<string> RunList(PageContent content, ICmsEditContext edit, Action<CmsImageTagHelper>? configure = null)
    {
        var accessor = new FakePageContentAccessor(content);
        var attributes = new TagHelperAttributeList { { "class", "card" } };
        var context = NewContext("li", attributes);
        var output = NewOutput("li", attributes, () => RenderImageChild(context));

        await new CmsListTagHelper(accessor, edit) { Key = "home.cards" }.ProcessAsync(context, output);
        return Render(output);

        async Task<TagHelperContent> RenderImageChild(TagHelperContext listContext)
        {
            var childAttributes = new TagHelperAttributeList();
            var childContext = NewContext("cms-image", childAttributes, new Dictionary<object, object>(listContext.Items));
            var childOutput = NewOutput("cms-image", childAttributes);
            var helper = new CmsImageTagHelper(accessor, edit, new ListLogger<CmsImageTagHelper>())
            {
                ItemField = "image",
                Sizes = "33vw",
                AltSource = "title",
            };
            configure?.Invoke(helper);
            await helper.ProcessAsync(childContext, childOutput);
            return Html(Render(childOutput));
        }
    }

    private static PageContent CardContent(params ContentListEntry[] entries) =>
        Content(
            lists: new() { ["home.cards"] = entries },
            images: new Dictionary<Guid, PageImage> { [Id] = Photo(Id) });

    [Fact]
    public async Task ListItem_Visitor_RendersItsImage_WithItsAlt()
    {
        var content = CardContent(
            Entry(1, true, ("image", Image(Id)), ("image-alt", Text("Card alt")), ("title", Text("Card"))),
            Entry(2, true, ("title", Text("No image"))));

        var html = await RunList(content, Visitor);

        Assert.Equal(
            $"<li class=\"card\"><img src=\"/media/photo-960.webp\" srcset=\"{Srcset}\" sizes=\"33vw\""
            + " width=\"3000\" height=\"2000\" alt=\"Card alt\" loading=\"lazy\" decoding=\"async\"></li>"
            + "<li class=\"card\"></li>",
            html);
    }

    [Fact]
    public async Task ListItem_Editor_UsesListRelativeKeys_AndPlaceholderInTemplate()
    {
        var content = CardContent(
            Entry(1, true, ("image", Image(Id)), ("image-alt", Text("Card alt")), ("title", Text("Card <1>"))));

        var html = await RunList(content, Editor);

        Assert.Contains(" data-cms-key=\"home.cards.image\"", html);
        Assert.Contains(" data-cms-alt-key=\"home.cards.image-alt\"", html);
        Assert.Contains(" data-cms-alt-proposal=\"Card &lt;1&gt;\"", html);
        Assert.Contains(
            "<template data-cms-list-template=\"home.cards\"><li class=\"card\" data-cms-list=\"home.cards\"><button type=\"button\" class=\"cms-image-placeholder\""
            + " data-cms-key=\"home.cards.image\"",
            html);
    }

    [Fact]
    public async Task NeitherKeyNorField_Throws() =>
        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(Content(), Visitor, h => h.Key = null));

    [Fact]
    public async Task BothKeyAndField_Throws() =>
        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(Content(), Visitor, h => h.ItemField = "image"));

    [Theory]
    [InlineData("home")]
    [InlineData("home.hero")]
    [InlineData("Home.Hero.Image")]
    public async Task InvalidKey_Throws(string key) =>
        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(Content(), Visitor, h => h.Key = key));

    [Fact]
    public async Task FieldOutsideList_Throws() =>
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Run(Content(), Visitor, h =>
            {
                h.Key = null;
                h.ItemField = "image";
            }));

    [Theory]
    [InlineData("auto")]
    [InlineData("Lazy ")]
    [InlineData("")]
    public async Task InvalidLoading_Throws(string loading) =>
        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(Content(), Visitor, h => h.Loading = loading));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task MissingSizes_Throws(string? sizes) =>
        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(Content(), Visitor, h => h.Sizes = sizes));

    [Fact]
    public async Task InvalidAltSourceKey_Throws() =>
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Run(Content(), Editor, h => h.AltSource = "title"));
}

[Collection(WebHostCollection.Name)]
public sealed class CmsImageWebHostTests(TempDatabaseWebApplicationFactory factory)
    : IClassFixture<TempDatabaseWebApplicationFactory>
{
    private async Task<Guid> SeedAssetAsync(int width = 3000, int height = 2000, int[]? widths = null)
    {
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Guid.NewGuid().ToByteArray()));
        var asset = MediaAsset.Create("boat.jpg", hash, "boat", widths ?? [480, 960, 1600, 2400], width, height, ".jpg");
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMediaAssetRepository>().Add(asset);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        return asset.Id;
    }

    private async Task Apply(params ContentChange[] changes)
    {
        using var scope = factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IContentService>()
            .ApplyChangesAsync(changes, new ContentEditor("image-test@example.com", null));
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
    }

    private async Task<HttpClient> NewAdminClientAsync(string email)
    {
        var client = NewClient(factory);
        await LoginWithTotpAsync(client, await AddTwoFactorAdminAsync(factory, email));
        return client;
    }

    [Fact]
    public async Task Sandbox_Visitor_RendersImages_FromTheStandaloneAndListFields()
    {
        var asset = await SeedAssetAsync();
        var item = Guid.NewGuid();
        await Apply(
            new SetField("shared.sandbox.image", "el", ContentType.Image, asset.ToString()),
            new SetField("shared.sandbox.image-alt", "el", ContentType.Text, "Sandbox alt"),
            new AddListItem("shared.sandbox-items", item),
            new SetField("shared.sandbox-items.image", "el", ContentType.Image, asset.ToString(), item),
            new SetField("shared.sandbox-items.image-alt", "el", ContentType.Text, "Item alt", item));

        var response = await NewClient(factory).GetAsync("/dev/cms-sandbox");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Matches(
            "<img class=\"sandbox-image\" src=\"/media/boat-[0-9a-f]{16}-960.webp\" srcset=\"/media/boat-[0-9a-f]{16}-480.webp 480w, "
            + "[^\"]*-2400.webp 2400w\" sizes=\"\\(min-width: 960px\\) 50vw, 100vw\" width=\"3000\" height=\"2000\" alt=\"Sandbox alt\" "
            + "loading=\"lazy\" decoding=\"async\">",
            html);
        Assert.Contains("sizes=\"(min-width: 720px) 33vw, 100vw\"", html);
        Assert.Contains("alt=\"Item alt\"", html);
        Assert.DoesNotContain("data-cms", html);
        Assert.DoesNotContain("cms-image-placeholder", html);
    }

    [Fact]
    public async Task Sandbox_Visitor_WithoutImages_HasNoImg()
    {
        using var fresh = new TempDatabaseWebApplicationFactory();

        var html = await NewClient(fresh).GetStringAsync("/dev/cms-sandbox");

        Assert.DoesNotContain("<img", html);
    }

    [Fact]
    public async Task Sandbox_Admin_GetsImageDataAttributes_AndPlaceholder()
    {
        var asset = await SeedAssetAsync();
        await Apply(
            new SetField("shared.sandbox.title", "el", ContentType.Text, "Τίτλος"),
            new SetField("shared.sandbox.image", "el", ContentType.Image, asset.ToString()),
            new SetField("shared.sandbox.image-alt", "el", ContentType.Text, "Alt EL"));
        var admin = await NewAdminClientAsync("image-admin@example.com");

        var html = await admin.GetStringAsync("/en/dev/cms-sandbox");

        Assert.Contains($"data-cms-key=\"shared.sandbox.image\" data-cms-type=\"image\" data-cms-value=\"{asset}\"", html);
        Assert.Contains("data-cms-alt-key=\"shared.sandbox.image-alt\" data-cms-alt=\"\" data-cms-alt-el=\"Alt EL\" data-cms-fallback", html);
        Assert.Contains("data-cms-alt-proposal=\"Τίτλος\"", html);
        Assert.Contains("class=\"cms-image-placeholder\" data-cms-key=\"shared.sandbox-items.image\"", html);
    }

    [Fact]
    public async Task Home_SeoImage_EmitsOgImageTags_ForVisitor()
    {
        var asset = await SeedAssetAsync();
        await Apply(
            new SetField("home.seo.image", "el", ContentType.Image, asset.ToString()),
            new SetField("home.seo.image-alt", "el", ContentType.Text, "Το σκάφος"));

        var html = await NewClient(factory).GetStringAsync("/");

        Assert.Matches("<meta property=\"og:image\" content=\"https://localhost/media/boat-[0-9a-f]{16}-1600.webp\" />", html);
        Assert.Contains("<meta property=\"og:image:width\" content=\"1600\" />", html);
        Assert.Contains("<meta property=\"og:image:height\" content=\"1067\" />", html);
        Assert.Contains("<meta property=\"og:image:alt\" content=\"Το σκάφος\" />", html);
    }
}
