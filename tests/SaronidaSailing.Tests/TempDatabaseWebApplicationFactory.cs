using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SaronidaSailing.Tests;

public sealed class TempDatabaseWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "saronida-tests-" + Guid.NewGuid().ToString("N"));

    public string DatabasePath => Path.Combine(_directory, "saronida.db");

    public string HangfirePath => Path.Combine(_directory, "hangfire.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Content", $"Data Source={DatabasePath};Pooling=False");
        builder.UseSetting("ConnectionStrings:Hangfire", HangfirePath);
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
