using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Web.Pages.Dev;
using static SaronidaSailing.Tests.AdminTestHelpers;

namespace SaronidaSailing.Tests;

[Collection(WebHostCollection.Name)]
public class CmsAdminRenderingTests(TempDatabaseWebApplicationFactory factory)
    : IClassFixture<TempDatabaseWebApplicationFactory>
{
    private static readonly string[] AdminMarkers = ["data-cms", "cms-edit", "cms-config", "cms-toolbar"];

    private async Task<HttpClient> NewAdminClientAsync(string email)
    {
        var client = NewClient(factory);
        await LoginWithTotpAsync(client, await AddTwoFactorAdminAsync(factory, email));
        return client;
    }

    private async Task Apply(params ContentChange[] changes)
    {
        using var scope = factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IContentService>()
            .ApplyChangesAsync(changes, new ContentEditor("render-test@example.com", null));
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
    }

    private static void AssertNoAdminMarkup(string html)
    {
        foreach (var marker in AdminMarkers)
        {
            Assert.DoesNotContain(marker, html);
        }
    }

    [Fact]
    public async Task Anonymous_Home_HasNoAdminMarkup()
    {
        var response = await NewClient(factory).GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertNoAdminMarkup(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Admin_Home_HasToolbarConfigAssets_AndNoStore()
    {
        var client = await NewAdminClientAsync("render-admin@example.com");

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains("class=\"cms-toolbar\" role=\"region\" aria-label=\"Admin toolbar\"", html);
        Assert.Matches(@"<link rel=""stylesheet"" href=""/css/cms-edit[^""]*\.css"" />", html);
        Assert.Matches(@"<script type=""module"" src=""/js/cms-edit[^""]*\.js""></script>", html);
        Assert.Contains("data-cms-action=\"toggle-edit\" aria-pressed=\"true\"", html);
        Assert.Matches(@"data-cms-action=""save""\s+disabled", html);
        Assert.Matches(@"data-cms-action=""discard""\s+disabled", html);
        Assert.Contains("<span class=\"cms-toolbar-counter\" data-cms-counter aria-live=\"polite\">No unsaved changes</span>", html);
        Assert.Contains("data-cms-status", html);
        Assert.Contains("data-cms-errors", html);
        Assert.Contains("href=\"/en/\" lang=\"en\">English</a>", html);
        Assert.Contains("href=\"/admin/jobs\"", html);

        var config = JsonDocument.Parse(Regex.Match(html, @"<script type=""application/json"" id=""cms-config"">(.*?)</script>").Groups[1].Value)
            .RootElement;
        Assert.Equal("home", config.GetProperty("pageKey").GetString());
        Assert.Equal("el", config.GetProperty("locale").GetString());
        Assert.Equal("/admin/api/content", config.GetProperty("saveUrl").GetString());
        Assert.Equal("/admin/api/content/history", config.GetProperty("historyUrl").GetString());
        Assert.Equal("/admin/api/content/saves/", config.GetProperty("savesUrl").GetString());
        Assert.False(string.IsNullOrWhiteSpace(config.GetProperty("antiforgeryToken").GetString()));
    }

    [Fact]
    public async Task Admin_EnglishHome_OffersGreekSwitch()
    {
        var client = await NewAdminClientAsync("render-en@example.com");

        var html = await client.GetStringAsync("/en/");

        Assert.Contains("href=\"/\" lang=\"el\">Ελληνικά</a>", html);
        Assert.Contains("\"locale\":\"en\"", html);
    }

    private static JsonElement ReadConfig(string html) =>
        JsonDocument.Parse(Regex.Match(html, @"<script type=""application/json"" id=""cms-config"">(.*?)</script>").Groups[1].Value)
            .RootElement;

    [Fact]
    public async Task Admin_Home_ConfigHasSeo_AndPageSettingsButton()
    {
        await Apply(
            new SetField("home.seo.title", "el", ContentType.Text, "Τίτλος"),
            new SetField("home.seo.title", "en", ContentType.Text, "Title"),
            new SetField("home.seo.description", "el", ContentType.Text, "Περιγραφή"));
        var admin = await NewAdminClientAsync("render-seo@example.com");

        var greek = await admin.GetStringAsync("/");
        var english = await admin.GetStringAsync("/en/");

        var el = ReadConfig(greek);
        var seo = el.GetProperty("seo");
        Assert.Equal("home", seo.GetProperty("pageKey").GetString());
        Assert.Equal("Τίτλος", seo.GetProperty("title").GetProperty("value").GetString());
        Assert.Equal(JsonValueKind.Null, seo.GetProperty("title").GetProperty("greek").ValueKind);
        Assert.Equal(" (ανοίγει σε νέα καρτέλα)", el.GetProperty("opensInNewTab").GetString());
        Assert.Contains("data-cms-action=\"page-settings\"", greek);

        var enSeo = ReadConfig(english).GetProperty("seo");
        Assert.Equal("Title", enSeo.GetProperty("title").GetProperty("value").GetString());
        Assert.Equal("", enSeo.GetProperty("description").GetProperty("value").GetString());
        Assert.Equal("Περιγραφή", enSeo.GetProperty("description").GetProperty("greek").GetString());
        Assert.Contains("data-cms-action=\"page-settings\"", english);
    }

    [Fact]
    public async Task Admin_Sandbox_HasNoSeoAndNoPageSettingsButton()
    {
        var admin = await NewAdminClientAsync("render-noseo@example.com");

        var html = await admin.GetStringAsync("/dev/cms-sandbox");

        Assert.Equal(JsonValueKind.Null, ReadConfig(html).GetProperty("seo").ValueKind);
        Assert.DoesNotContain("page-settings", html);
    }

    [Fact]
    public async Task Admin_NextMissingButton_OnlyOnEnglishPages()
    {
        var admin = await NewAdminClientAsync("render-missing@example.com");

        Assert.Contains("data-cms-action=\"next-missing\"", await admin.GetStringAsync("/en/"));
        Assert.DoesNotContain("next-missing", await admin.GetStringAsync("/"));
    }

    [Theory]
    [InlineData("/lib/quill/quill.js")]
    [InlineData("/lib/quill/quill.snow.css")]
    [InlineData("/lib/quill/LICENSE")]
    public async Task VendoredQuill_IsServed(string path)
    {
        var response = await NewClient(factory).GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SignedInWithoutTwoFactor_GetsVisitorPage()
    {
        var client = NewClient(factory);
        await AddAdminAsync(factory, "render-no2fa@example.com");
        Assert.Equal(HttpStatusCode.Redirect, (await LoginAsync(client, "render-no2fa@example.com")).StatusCode);

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertNoAdminMarkup(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task OutputCache_NeverMixesAdminAndVisitorMarkup()
    {
        var anonymous = NewClient(factory);
        AssertNoAdminMarkup(await anonymous.GetStringAsync("/"));

        var admin = await NewAdminClientAsync("render-cache@example.com");
        Assert.Contains("cms-toolbar", await admin.GetStringAsync("/"));

        AssertNoAdminMarkup(await anonymous.GetStringAsync("/"));
        Assert.Contains("cms-toolbar", await admin.GetStringAsync("/"));
    }

    [Fact]
    public async Task ToolbarLogout_PostsAndSignsOut()
    {
        var admin = await NewAdminClientAsync("render-logout@example.com");
        var html = await admin.GetStringAsync("/");
        var form = Regex.Match(html, @"<form[^>]*action=""/admin/logout""[^>]*>(.*?)</form>", RegexOptions.Singleline);
        Assert.True(form.Success);
        Assert.Contains("method=\"post\"", form.Value);
        Assert.Contains("Log out</button>", form.Value);

        var response = await admin.PostAsync("/admin/logout", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["__RequestVerificationToken"] = ExtractToken(form.Value) }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", Location(response));
        AssertNoAdminMarkup(await admin.GetStringAsync("/"));
    }

    [Fact]
    public async Task Sandbox_Anonymous_RendersVisitorMarkup_InGreekAndEnglish()
    {
        var client = NewClient(factory);
        await Apply(new SetField("shared.sandbox.title", "el", ContentType.Text, "Δοκιμή"));

        var greek = await client.GetAsync("/dev/cms-sandbox");
        var english = await client.GetAsync("/en/dev/cms-sandbox");

        Assert.Equal(HttpStatusCode.OK, greek.StatusCode);
        Assert.Equal(HttpStatusCode.OK, english.StatusCode);
        var greekHtml = await greek.Content.ReadAsStringAsync();
        var englishHtml = await english.Content.ReadAsStringAsync();
        Assert.Contains("<html lang=\"el\">", greekHtml);
        Assert.Contains("<html lang=\"en\">", englishHtml);
        Assert.Contains("<meta name=\"robots\" content=\"noindex, nofollow\" />", greekHtml);
        Assert.Contains("<h1>Δοκιμή</h1>", greekHtml);
        Assert.Contains("<h1>Δοκιμή</h1>", englishHtml);
        AssertNoAdminMarkup(greekHtml);
    }

    [Fact]
    public async Task Sandbox_Admin_SeesEditorMarkup_PlaceholdersAndInactiveItems()
    {
        var item = Guid.NewGuid();
        await Apply(
            new AddListItem("shared.sandbox-items", item),
            new SetField("shared.sandbox-items.title", "el", ContentType.Text, "Κρυφό", item),
            new SetListItemActive("shared.sandbox-items", item, false),
            new SetSectionActive("shared.sandbox-list", false));
        var admin = await NewAdminClientAsync("render-sandbox@example.com");

        var html = await admin.GetStringAsync("/dev/cms-sandbox");

        Assert.Contains("data-cms-section=\"shared.sandbox\"", html);
        Assert.Contains("data-cms-section=\"shared.sandbox-list\" data-cms-active=\"false\"", html);
        Assert.Contains("data-cms-key=\"shared.sandbox.body\" data-cms-type=\"richText\" data-cms-empty", html);
        Assert.Contains("data-cms-key=\"shared.sandbox.cta\" data-cms-type=\"link\" data-cms-empty", html);
        Assert.Contains($"data-cms-item=\"{item}\" data-cms-active=\"false\"", html);
        Assert.Contains("data-cms-key=\"shared.sandbox-items.title\" data-cms-type=\"text\">Κρυφό</h3>", html);
        Assert.Contains("<template data-cms-list-template=\"shared.sandbox-items\">", html);
        Assert.Contains("<template data-cms-list-template=\"shared.sandbox-steps\">", html);
        Assert.Contains("data-cms-section-link=\"shared.sandbox-list\" data-cms-active=\"false\"", html);
        Assert.DoesNotContain("cms-section-link=", html.Replace("data-cms-section-link=", ""));
    }

    [Fact]
    public async Task Sandbox_ReturnsNotFound_OutsideDevelopment()
    {
        using var production = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        var client = NewClientFromIp(production, "10.201.0.1");

        var response = await client.GetAsync("/dev/cms-sandbox");
        var english = await client.GetAsync("/en/dev/cms-sandbox");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, english.StatusCode);
    }

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Production", false)]
    public void SandboxModel_OnlyServesInDevelopment(string environment, bool served)
    {
        var model = new CmsSandboxModel(new FakeEnvironment(environment));

        Assert.Equal(served, model.OnGet() is Microsoft.AspNetCore.Mvc.RazorPages.PageResult);
        Assert.Equal(!served, model.OnGet() is Microsoft.AspNetCore.Mvc.NotFoundResult);
    }

    private sealed class FakeEnvironment(string name) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "test";

        public string WebRootPath { get; set; } = "";

        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = null!;

        public string ContentRootPath { get; set; } = "";

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
