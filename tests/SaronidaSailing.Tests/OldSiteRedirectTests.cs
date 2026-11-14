using System.Net;
using static SaronidaSailing.Tests.AdminTestHelpers;

namespace SaronidaSailing.Tests;

[Collection(WebHostCollection.Name)]
public class OldSiteRedirectTests(ErrorPagesWebApplicationFactory factory) : IClassFixture<ErrorPagesWebApplicationFactory>
{
    private async Task AssertRedirect(string path, string location, HttpClient? client = null)
    {
        var response = await (client ?? NewClient(factory)).GetAsync(path);

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal(location, response.Headers.Location!.OriginalString);
        Assert.Equal("", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/index.php", "/")]
    [InlineData("/index.php/el", "/")]
    [InlineData("/index.php/en", "/en/")]
    [InlineData("/index.php/el/yacht-charter-gr", "/ekdromes")]
    [InlineData("/index.php/el/daily-excursions-gr", "/ekdromes")]
    [InlineData("/index.php/en/yacht-charter", "/en/trips")]
    [InlineData("/index.php/en/daily-excursions", "/en/trips")]
    [InlineData("/index.php/el/sailing-yacht-2", "/skafos")]
    [InlineData("/index.php/en/sailing-yacht", "/en/boat")]
    [InlineData("/index.php/el/contacts-gr", "/epikoinonia")]
    [InlineData("/index.php/en/contacts", "/en/contact")]
    [InlineData("/index.php/el/privacy-policy", "/oroi")]
    [InlineData("/index.php/en/privacy-policy", "/en/terms")]
    [InlineData("/index.php/el/friends-gr", "/")]
    [InlineData("/index.php/en/friends-en", "/en/")]
    public async Task TableRow_RedirectsToTheAbsoluteNewAddress(string path, string target) =>
        await AssertRedirect(path, "https://localhost" + target);

    [Theory]
    [InlineData("/INDEX.php/EL/Contacts-GR", "/epikoinonia")]
    [InlineData("/index.php/el/contacts-gr/", "/epikoinonia")]
    [InlineData("/index.php/", "/")]
    [InlineData("/index.php/en/", "/en/")]
    [InlineData("/Index.PHP/en/Sailing-Yacht/", "/en/boat")]
    public async Task CaseAndTrailingSlash_DoNotMatter(string path, string target) =>
        await AssertRedirect(path, "https://localhost" + target);

    [Theory]
    [InlineData("/index.php/el/contacts-gr?format=vcf", "/epikoinonia")]
    [InlineData("/index.php/en/contacts?format=vcf", "/en/contact")]
    [InlineData("/index.php/el/sailing-yacht-2?Itemid=5", "/skafos")]
    [InlineData("/index.php?option=com_content&Itemid=5", "/")]
    public async Task QueryString_IsDropped(string path, string target) =>
        await AssertRedirect(path, "https://localhost" + target);

    [Fact]
    public async Task AnyHost_RedirectsToTheCanonicalSite()
    {
        var client = NewClient(factory);
        client.DefaultRequestHeaders.Host = "www.saronidasailing.gr";

        await AssertRedirect("/index.php/en/contacts", "https://localhost/en/contact", client);
    }

    [Fact]
    public async Task Head_IsRedirectedToo()
    {
        var response = await NewClient(factory).SendAsync(new HttpRequestMessage(HttpMethod.Head, "/index.php/el/contacts-gr"));

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("https://localhost/epikoinonia", response.Headers.Location!.OriginalString);
    }

    [Theory]
    [InlineData("/index.php/el/component/users", "Η σελίδα δεν βρέθηκε")]
    [InlineData("/index.php/en/component/users", "Η σελίδα δεν βρέθηκε")]
    [InlineData("/index.php/en/component/users/", "Η σελίδα δεν βρέθηκε")]
    [InlineData("/index.php/EN/Component/Users/login?return=x", "Η σελίδα δεν βρέθηκε")]
    [InlineData("/index.php/el/component/users/registration", "Η σελίδα δεν βρέθηκε")]
    public async Task JoomlaUsers_AreGone_WithTheNotFoundPage(string path, string title)
    {
        var response = await NewClient(factory).GetAsync(path);

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Contains(title, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/index.php/el/whatever", "/")]
    [InlineData("/index.php/el/whatever/deeper?x=1", "/")]
    [InlineData("/index.php/en/whatever", "/en/")]
    [InlineData("/index.php/en/component/other", "/en/")]
    [InlineData("/index.php/whatever", "/")]
    [InlineData("/index.php/component/users", "/")]
    [InlineData("/index.php/english", "/")]
    public async Task OtherOldPaths_FallBackToHome(string path, string target) =>
        await AssertRedirect(path, "https://localhost" + target);

    [Fact]
    public async Task NonGetMethods_PassThroughUnchanged()
    {
        var response = await NewClient(factory).PostAsync("/index.php/el/contacts-gr", new StringContent(""));

        Assert.NotEqual(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Theory]
    [InlineData("/", HttpStatusCode.OK)]
    [InlineData("/en/trips", HttpStatusCode.OK)]
    [InlineData("/robots.txt", HttpStatusCode.OK)]
    [InlineData("/index.phpx", HttpStatusCode.NotFound)]
    [InlineData("/nothing-here", HttpStatusCode.NotFound)]
    public async Task OtherPaths_AreUnaffected(string path, HttpStatusCode expected)
    {
        var response = await NewClient(factory).GetAsync(path);

        Assert.Equal(expected, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }
}

[Collection(WebHostCollection.Name)]
public class OldSiteRedirectPreviewTests(PreviewWebApplicationFactory factory) : IClassFixture<PreviewWebApplicationFactory>
{
    [Theory]
    [InlineData("/index.php/el/contacts-gr")]
    [InlineData("/index.php/el/component/users")]
    public async Task InPreview_TheTableIsOff(string path)
    {
        var response = await NewClient(factory).GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }
}
