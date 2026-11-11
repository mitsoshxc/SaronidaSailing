using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NLog;
using SaronidaSailing.Core.Interfaces;

namespace SaronidaSailing.Tests;

[Collection(WebHostCollection.Name)]
public class LoggingTests(TempDatabaseWebApplicationFactory factory) : IClassFixture<TempDatabaseWebApplicationFactory>
{
    private static string Today => DateTime.UtcNow.ToString("yyyy-MM-dd");

    [Fact]
    public void AuditRecord_WritesLineToAuditFileOnly()
    {
        var audit = factory.Services.GetRequiredService<IAuditLog>();

        audit.Record("login-succeeded", "a@b.gr", "1.2.3.4", "from test");
        audit.Record("admin-list", null, null);
        LogManager.Flush();

        var lines = ReadLines(factory.LogsPath, $"audit-{Today}.log");
        Assert.Matches(
            new Regex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{7}Z\|test-pod\|login-succeeded\|user=a@b\.gr\|ip=1\.2\.3\.4\|from test$"),
            lines.Single(l => l.Contains("login-succeeded")));
        Assert.EndsWith("|test-pod|admin-list|user=-|ip=-", lines.Single(l => l.Contains("admin-list")));
        Assert.DoesNotContain(ReadLines(factory.LogsPath, $"app-{Today}.log"), l => l.Contains("login-succeeded"));
    }

    [Fact]
    public void NLogTimeSource_IsAccurateUtc() =>
        Assert.IsType<NLog.Time.AccurateUtcTimeSource>(NLog.Time.TimeSource.Current);

    [Fact]
    public void AuditRecords_InTightLoop_HaveMonotonicTimestamps()
    {
        var audit = factory.Services.GetRequiredService<IAuditLog>();

        for (var i = 0; i < 20; i++)
        {
            audit.Record("tight-loop-test", null, null, i.ToString());
        }

        LogManager.Flush();

        var stamps = ReadLines(factory.LogsPath, $"audit-{Today}.log")
            .Where(l => l.Contains("|tight-loop-test|"))
            .Select(l => DateTime.Parse(l.Split('|')[0], null, System.Globalization.DateTimeStyles.RoundtripKind))
            .ToList();
        Assert.Equal(20, stamps.Count);
        Assert.Equal(stamps.OrderBy(t => t), stamps);
        Assert.True(stamps.Distinct().Count() > 1);
    }

    [Fact]
    public void AuditRecord_SanitizesFieldsAgainstLogInjection()
    {
        var audit = factory.Services.GetRequiredService<IAuditLog>();

        audit.Record("sanitize-test", "evil\r\n2026-01-01|fake-event|user=x\t\u2028", "1.2.3.4|5", new string('d', 1000));
        LogManager.Flush();

        var lines = ReadLines(factory.LogsPath, $"audit-{Today}.log");
        var line = lines.Single(l => l.Contains("sanitize-test"));
        var fields = line.Split('|');
        Assert.Equal(6, fields.Length);
        Assert.Equal("test-pod", fields[1]);
        Assert.Equal("user=evil  2026-01-01/fake-event/user=x  ", fields[3]);
        Assert.Equal("ip=1.2.3.4/5", fields[4]);
        Assert.Equal(256, fields[5].Length);
        Assert.DoesNotContain(lines, l => l.StartsWith("2026-01-01"));
    }

    [Fact]
    public void Warning_WritesToAppFile_AndInformationWritesNowhere()
    {
        var logger = factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Saronida.Tests.Ordinary");

        logger.LogWarning("warning-marker-{Id}", 1);
        logger.LogInformation("information-marker-{Id}", 2);
        LogManager.Flush();

        var app = ReadLines(factory.LogsPath, $"app-{Today}.log");
        Assert.Contains(app, l => l.Contains("|test-pod|WARN|Saronida.Tests.Ordinary|warning-marker-1"));
        Assert.DoesNotContain(app, l => l.Contains("information-marker"));
        var all = Directory.GetFiles(factory.LogsPath).SelectMany(f => ReadLines(Path.GetDirectoryName(f)!, Path.GetFileName(f)));
        Assert.DoesNotContain(all, l => l.Contains("information-marker"));
    }

    private static string[] ReadLines(string folder, string fileName)
    {
        var path = Path.Combine(folder, fileName);
        if (!File.Exists(path))
        {
            return [];
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
