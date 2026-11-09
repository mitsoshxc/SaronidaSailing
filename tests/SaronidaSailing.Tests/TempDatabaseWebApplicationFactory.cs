using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SaronidaSailing.Web.Health;

namespace SaronidaSailing.Tests;

public sealed class TestRemoteIpStartupFilter : IStartupFilter
{
    public const string HeaderName = "X-Test-Remote-Ip";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, nextMiddleware) =>
        {
            if (context.Request.Headers.TryGetValue(HeaderName, out var ip) && IPAddress.TryParse(ip.ToString(), out var address))
            {
                context.Connection.RemoteIpAddress = address;
            }

            return nextMiddleware();
        });
        next(app);
    };
}

public sealed class TempDatabaseWebApplicationFactory: WebApplicationFactory<Program>
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "saronida-tests-" + Guid.NewGuid().ToString("N"));

    public TempDatabaseWebApplicationFactory() => ClientOptions.BaseAddress = new Uri("https://localhost");

    public string DatabasePath => Path.Combine(_directory, "saronida.db");

    public string HangfirePath => Path.Combine(_directory, "hangfire.db");

    public string MediaPath => Path.Combine(_directory, "media");

    public string LogsPath => Path.Combine(_directory, "logs");

    public string KeysPath => Path.Combine(_directory, "keys");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(MediaPath);

        builder.UseSetting("Logs:Path", LogsPath);
        builder.UseSetting("ConnectionStrings:Content", $"Data Source={DatabasePath};Pooling=False");
        builder.UseSetting("ConnectionStrings:Hangfire", HangfirePath);
        builder.UseSetting("Media:RootPath", MediaPath);
        builder.UseSetting("DataProtection:KeysPath", KeysPath);
        builder.UseSetting("Site:BaseUrl", "https://localhost");
        builder.UseSetting("Health:HomePageUrl", "http://localhost/");

        builder.ConfigureTestServices(services =>
        {
            services
                .AddHttpClient(HomePageHealthCheck.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(sp => ((TestServer)sp.GetRequiredService<IServer>()).CreateHandler());
            services.AddTransient<IStartupFilter, TestRemoteIpStartupFilter>();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            DeleteDirectory();
        }
    }

    // Hangfire.Storage.SQLite releases hangfire.db a moment after the host has stopped (its background threads
    // finish asynchronously), so the delete is retried briefly; a persistent lock still fails the test run.
    private void DeleteDirectory()
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (Directory.Exists(_directory))
        {
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(50);
            }
        }
    }
}
