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

public class TempDatabaseWebApplicationFactory: WebApplicationFactory<Program>
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "saronida-tests-" + Guid.NewGuid().ToString("N"));

    private readonly object _databaseLock = new();
    private string? _contentConnectionString;
    private bool _ownsDatabase = true;

    public TempDatabaseWebApplicationFactory() => ClientOptions.BaseAddress = new Uri("https://localhost");

    protected virtual bool UseEmptyDatabase => false;

    protected virtual bool? MigrateAtStartup => null;

    public string ContentConnectionString
    {
        get
        {
            lock (_databaseLock)
            {
                // Task.Run keeps the blocking wait off xunit's synchronization context.
                return _contentConnectionString ??= Task.Run(
                    () => UseEmptyDatabase
                        ? PostgresTestServer.CreateEmptyDatabaseAsync()
                        : PostgresTestServer.CreateDatabaseAsync()).GetAwaiter().GetResult();
            }
        }
    }

    public void UseDatabaseOf(TempDatabaseWebApplicationFactory owner)
    {
        lock (_databaseLock)
        {
            _contentConnectionString = owner.ContentConnectionString;
            _ownsDatabase = false;
        }
    }

    public string MediaPath => Path.Combine(_directory, "media");

    public string LogsPath => Path.Combine(_directory, "logs");

    public string KeysPath => Path.Combine(_directory, "keys");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(MediaPath);

        builder.UseSetting("Logs:Path", LogsPath);
        builder.UseSetting("ConnectionStrings:Content", ContentConnectionString);
        builder.UseSetting("Media:RootPath", MediaPath);
        if (MigrateAtStartup is { } migrate)
        {
            builder.UseSetting("Database:MigrateAtStartup", migrate ? "true" : "false");
        }
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
            DropDatabase();
        }
    }

    private void DropDatabase()
    {
        if (_contentConnectionString is null || !_ownsDatabase)
        {
            return;
        }

        var name = PostgresTestServer.DatabaseName(_contentConnectionString);
        Task.Run(() => PostgresTestServer.DropDatabaseAsync(name)).GetAwaiter().GetResult();
    }

    private void DeleteDirectory()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
