using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using SaronidaSailing.Web.Content;

namespace SaronidaSailing.Tests;

[Collection(WebHostCollection.Name)]
public class PageContentAccessorWebHostTests(TempDatabaseWebApplicationFactory factory)
    : IClassFixture<TempDatabaseWebApplicationFactory>
{
    [Fact]
    public async Task RequestScope_ResolvesAccessor_AndLoadsSharedContentOnce()
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("el");
            using var scope = factory.Services.CreateScope();
            var accessor = scope.ServiceProvider.GetRequiredService<IPageContentAccessor>();

            var first = await accessor.GetAsync();
            var second = await scope.ServiceProvider.GetRequiredService<IPageContentAccessor>().GetAsync();

            Assert.Equal("shared", first.PageKey);
            Assert.Equal("el", first.Locale);
            Assert.Same(first, second);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }
}
