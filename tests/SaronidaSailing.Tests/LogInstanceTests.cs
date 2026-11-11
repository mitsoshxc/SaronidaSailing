using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NLog;
using SaronidaSailing.Core.Interfaces;

namespace SaronidaSailing.Tests;

public sealed class InvalidLogInstanceWebApplicationFactory : TempDatabaseWebApplicationFactory
{
    protected override string? LogInstanceName => "a|b";
}

public sealed class DefaultLogInstanceWebApplicationFactory : TempDatabaseWebApplicationFactory
{
    protected override string? LogInstanceName => null;
}

[Collection(WebHostCollection.Name)]
public class InvalidLogInstanceTests
{
    [Fact]
    public void Startup_WithPipeInLogsInstance_FailsAndNamesTheKey()
    {
        using var factory = new InvalidLogInstanceWebApplicationFactory();

        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        var messages = new List<string>();
        for (var current = ex; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        Assert.Contains(messages, m => m.Contains("Logs:Instance", StringComparison.Ordinal));
    }
}

[Collection(WebHostCollection.Name)]
public class DefaultLogInstanceTests(DefaultLogInstanceWebApplicationFactory factory) : IClassFixture<DefaultLogInstanceWebApplicationFactory>
{
    [Fact]
    public void WithoutLogsInstance_LinesCarryTheMachineName()
    {
        using var _ = factory.CreateClient();
        factory.Services.GetRequiredService<IAuditLog>().Record("machine-name-check", "cli", null);
        factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Saronida.Tests.Ordinary").LogWarning("machine-name-check");
        LogManager.Flush();

        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var audit = ReadAllLines(Path.Combine(factory.LogsPath, $"audit-{today}.log")).Single(l => l.Contains("machine-name-check"));
        var app = ReadAllLines(Path.Combine(factory.LogsPath, $"app-{today}.log")).Single(l => l.Contains("machine-name-check"));
        Assert.Equal(Environment.MachineName, audit.Split('|')[1]);
        Assert.Equal(Environment.MachineName, app.Split('|')[1]);
    }

    private static string[] ReadAllLines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
