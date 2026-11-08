using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SaronidaSailing.Tests;

public class HomePageTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Get_Root_ReturnsOk()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Saronida Sailing", await response.Content.ReadAsStringAsync());
    }
}
