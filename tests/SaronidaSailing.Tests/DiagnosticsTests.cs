using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NLog;
using NLog.Config;
using NLog.Targets;
using SaronidaSailing.Web.Diagnostics;
using SaronidaSailing.Web.Identity;
using static SaronidaSailing.Tests.AdminTestHelpers;

namespace SaronidaSailing.Tests;

internal sealed class DiagnosticsLogCapture : IDisposable
{
    private readonly LogFactory _factory = new();
    private readonly MemoryTarget _target = new("memory")
    {
        Layout = "${level:uppercase=true}|${logger}|${message}${onexception:inner=|${exception:format=message}}",
    };

    public DiagnosticsLogCapture()
    {
        var configuration = new LoggingConfiguration(_factory);
        configuration.AddRule(LogLevel.Trace, LogLevel.Fatal, _target);
        _factory.Configuration = configuration;
    }

    public IList<string> Lines => _target.Logs;

    public Logger Logger(string name) => _factory.GetLogger(name);

    public void Dispose() => _factory.Dispose();
}

public class ResourceSnapshotUnitTests
{
    private const long MiB = 1024 * 1024;

    private sealed class FakeReadings : IProcessReadings
    {
        public ProcessSample Sample { get; set; } = new(100 * MiB, 50 * MiB, 1000 * MiB, 750 * MiB, TimeSpan.Zero, 2, 0, 0, 0, 8, 0);

        public Exception? Failure { get; set; }

        public ProcessSample Read() => Failure is null ? Sample : throw Failure;
    }

    private sealed class Harness : IDisposable
    {
        private readonly DiagnosticsLogCapture _capture = new();

        public Harness()
        {
            Service = new ResourceSnapshotService(
                Readings, Counters, Time, Options.Create(new DiagnosticsOptions()),
                _capture.Logger(ResourceSnapshotService.MetricsLoggerName),
                _capture.Logger(RequestDiagnosticsMiddleware.LoggerName));
        }

        public FakeReadings Readings { get; } = new();

        public RequestCounters Counters { get; } = new();

        public FakeTimeProvider Time { get; } = new();

        public ResourceSnapshotService Service { get; }

        public IEnumerable<string> Metrics => _capture.Lines.Where(l => l.StartsWith("INFO|Metrics|"));

        public List<string> Alerts => _capture.Lines.Where(l => l.Contains("|Diagnostics|")).ToList();

        public void Snapshot(TimeSpan after)
        {
            Time.Advance(after);
            Service.TakeSnapshot();
        }

        public void Dispose() => _capture.Dispose();
    }

    [Fact]
    public void BuildMessage_FormatsAllKeysWithInvariantCulture()
    {
        var sample = new ProcessSample(312 * MiB, 120 * MiB, 1536 * MiB, 1152 * MiB, TimeSpan.Zero, 2, 12, 3, 1, 9, 0);

        var message = ResourceSnapshotService.BuildMessage(sample, 20.3125, 4.14, 1, new RequestTotals(37, 0, 0));

        Assert.Equal(
            "workingSetMiB=312 gcHeapMiB=120 memoryLimitMiB=1536 gcHeapLimitMiB=1152 memoryPct=20.3 cpuPct=4.1 gc0=12 gc1=3 gc2=1 threads=9 queue=0 inFlight=1 requestsPerMin=37 errors5xxPerMin=0 rateLimitedPerMin=0",
            message);
    }

    [Fact]
    public void Snapshot_ComputesCpuFromTheTimeProviderAndProcessorCount()
    {
        using var harness = new Harness();
        harness.Readings.Sample = harness.Readings.Sample with { CpuTime = TimeSpan.FromSeconds(1) };
        harness.Service.TakeSnapshot();

        harness.Readings.Sample = harness.Readings.Sample with { CpuTime = TimeSpan.FromSeconds(4) };
        harness.Snapshot(TimeSpan.FromSeconds(60));

        Assert.Contains(" cpuPct=2.5 ", harness.Metrics.Last());
    }

    [Fact]
    public void MemoryAbove_Threshold_WarnsOnceRepeatsAfterTenMinutesAndLogsRecoveryOnce()
    {
        using var harness = new Harness();
        harness.Readings.Sample = harness.Readings.Sample with { WorkingSetBytes = 900 * MiB };

        harness.Snapshot(TimeSpan.FromMinutes(1));
        Assert.Equal(["WARN|Diagnostics|Memory high memoryPct=90.0 workingSetMiB=900 memoryLimitMiB=1000"], harness.Alerts);

        harness.Snapshot(TimeSpan.FromMinutes(5));
        Assert.Single(harness.Alerts);

        harness.Snapshot(TimeSpan.FromMinutes(5));
        Assert.Equal(2, harness.Alerts.Count);
        Assert.StartsWith("WARN|Diagnostics|Memory high ", harness.Alerts[1]);

        harness.Readings.Sample = harness.Readings.Sample with { WorkingSetBytes = 500 * MiB };
        harness.Snapshot(TimeSpan.FromMinutes(1));
        harness.Snapshot(TimeSpan.FromMinutes(1));

        Assert.Equal(3, harness.Alerts.Count);
        Assert.Equal("INFO|Diagnostics|Memory back to normal memoryPct=50.0 workingSetMiB=500 memoryLimitMiB=1000", harness.Alerts[2]);
        Assert.Equal(5, harness.Metrics.Count());
    }

    [Fact]
    public void ThreadPoolQueue_AtThreshold_WarnsOnceRepeatsAfterTenMinutesAndLogsRecoveryOnce()
    {
        using var harness = new Harness();
        harness.Readings.Sample = harness.Readings.Sample with { QueueLength = 100 };

        harness.Snapshot(TimeSpan.FromMinutes(1));
        Assert.Equal(["WARN|Diagnostics|Thread pool queue high queue=100 threads=8"], harness.Alerts);

        harness.Snapshot(TimeSpan.FromMinutes(5));
        Assert.Single(harness.Alerts);

        harness.Snapshot(TimeSpan.FromMinutes(5));
        Assert.Equal(2, harness.Alerts.Count);

        harness.Readings.Sample = harness.Readings.Sample with { QueueLength = 3 };
        harness.Snapshot(TimeSpan.FromMinutes(1));
        harness.Snapshot(TimeSpan.FromMinutes(1));

        Assert.Equal(3, harness.Alerts.Count);
        Assert.Equal("INFO|Diagnostics|Thread pool queue back to normal queue=3 threads=8", harness.Alerts[2]);
    }

    [Fact]
    public void PerMinuteCounters_ResetAfterASnapshot_InFlightDoesNot()
    {
        using var harness = new Harness();
        harness.Counters.RequestStarted();
        harness.Counters.RequestStarted();
        harness.Counters.RequestStarted();
        harness.Counters.RequestStarted();
        harness.Counters.RequestCompleted(200);
        harness.Counters.RequestCompleted(500);
        harness.Counters.RequestCompleted(429);

        harness.Snapshot(TimeSpan.FromMinutes(1));
        harness.Snapshot(TimeSpan.FromMinutes(1));

        var lines = harness.Metrics.ToList();
        Assert.Contains(" inFlight=1 requestsPerMin=3 errors5xxPerMin=1 rateLimitedPerMin=1", lines[0]);
        Assert.Contains(" inFlight=1 requestsPerMin=0 errors5xxPerMin=0 rateLimitedPerMin=0", lines[1]);
    }

    [Fact]
    public void FailingReadings_LogAWarningAndDoNotThrow()
    {
        using var harness = new Harness();
        harness.Readings.Failure = new InvalidOperationException("no cgroup");

        harness.Service.TakeSnapshot();

        Assert.Equal(["WARN|Diagnostics|Resource snapshot failed|no cgroup"], harness.Alerts);
    }

    [Fact]
    public void Options_AreValidated_AllMustBePositive()
    {
        var validator = new DiagnosticsOptionsValidator();

        Assert.True(validator.Validate(null, new DiagnosticsOptions()).Succeeded);
        var result = validator.Validate(null, new DiagnosticsOptions { SnapshotIntervalSeconds = 0, WarningRepeatMinutes = -1 });

        Assert.True(result.Failed);
        Assert.Contains("Diagnostics:SnapshotIntervalSeconds must be greater than 0.", result.Failures!);
        Assert.Contains("Diagnostics:WarningRepeatMinutes must be greater than 0.", result.Failures!);
    }
}

public class HealthTransitionPublisherTests
{
    private static HealthReport Report(params (string Name, HealthStatus Status, string? Description, Exception? Exception)[] entries) =>
        new(entries.ToDictionary(
                e => e.Name,
                e => new HealthReportEntry(e.Status, e.Description, TimeSpan.Zero, e.Exception, null)),
            TimeSpan.Zero);

    private static async Task PublishAsync(HealthTransitionPublisher publisher, HealthReport report) =>
        await publisher.PublishAsync(report, CancellationToken.None);

    [Fact]
    public async Task HealthyToUnhealthy_WarnsWithCheckNameDescriptionAndException_ThenStaysQuiet_ThenLogsRecovery()
    {
        using var capture = new DiagnosticsLogCapture();
        var publisher = new HealthTransitionPublisher(capture.Logger(RequestDiagnosticsMiddleware.LoggerName));
        var healthy = Report(("database", HealthStatus.Healthy, null, null), ("media", HealthStatus.Healthy, null, null));
        var broken = Report(
            ("database", HealthStatus.Unhealthy, "connection refused", new InvalidOperationException("boom")),
            ("media", HealthStatus.Healthy, null, null));

        await PublishAsync(publisher, healthy);
        Assert.Empty(capture.Lines);

        await PublishAsync(publisher, broken);
        Assert.Equal(
            [
                "WARN|Diagnostics|Health check database Healthy -> Unhealthy: connection refused|boom",
                "WARN|Diagnostics|Health check overall Healthy -> Unhealthy: failing: database",
            ],
            capture.Lines);

        await PublishAsync(publisher, broken);
        Assert.Equal(2, capture.Lines.Count);

        await PublishAsync(publisher, healthy);
        Assert.Equal(
            [
                "INFO|Diagnostics|Health check database recovered: Unhealthy -> Healthy",
                "INFO|Diagnostics|Health check overall recovered: Unhealthy -> Healthy",
            ],
            capture.Lines.Skip(2));
    }

    [Fact]
    public async Task FirstReport_LogsOnlyNonHealthyEntriesAsAtStartup()
    {
        using var capture = new DiagnosticsLogCapture();
        var publisher = new HealthTransitionPublisher(capture.Logger(RequestDiagnosticsMiddleware.LoggerName));

        await PublishAsync(publisher, Report(
            ("database", HealthStatus.Unhealthy, "down", new InvalidOperationException("boom")),
            ("media", HealthStatus.Healthy, null, null)));

        Assert.Equal(["WARN|Diagnostics|Health check database Unhealthy at startup: down|boom"], capture.Lines);
    }

    [Fact]
    public async Task FirstReport_AllHealthy_LogsNothing()
    {
        using var capture = new DiagnosticsLogCapture();
        var publisher = new HealthTransitionPublisher(capture.Logger(RequestDiagnosticsMiddleware.LoggerName));

        await PublishAsync(publisher, Report(("database", HealthStatus.Healthy, null, null)));

        Assert.Empty(capture.Lines);
    }
}

[Collection(WebHostCollection.Name)]
public class MetricsLoggingTests(TempDatabaseWebApplicationFactory factory) : IClassFixture<TempDatabaseWebApplicationFactory>
{
    [Fact]
    public void Snapshot_WritesOneLineWithAllKeysToTheMetricsFileOnly()
    {
        var before = ReadLogLines(factory.LogsPath, "metrics").Length;

        factory.Services.GetRequiredService<ResourceSnapshotService>().TakeSnapshot();

        var lines = ReadLogLines(factory.LogsPath, "metrics");
        Assert.True(lines.Length > before);
        Assert.Matches(
            new Regex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{7}Z\|test-pod\|workingSetMiB=\d+ gcHeapMiB=\d+ memoryLimitMiB=\d+ gcHeapLimitMiB=\d+ memoryPct=\d+\.\d cpuPct=\d+\.\d gc0=\d+ gc1=\d+ gc2=\d+ threads=\d+ queue=\d+ inFlight=\d+ requestsPerMin=\d+ errors5xxPerMin=\d+ rateLimitedPerMin=\d+$"),
            lines[^1]);
        Assert.DoesNotContain(ReadLogLines(factory.LogsPath, "app"), l => l.Contains("workingSetMiB="));
        Assert.DoesNotContain(ReadLogLines(factory.LogsPath, "audit"), l => l.Contains("workingSetMiB="));
    }
}

[Collection(WebHostCollection.Name)]
public class MetricsRetentionTests(TempDatabaseWebApplicationFactory factory) : IClassFixture<TempDatabaseWebApplicationFactory>
{
    [Fact]
    public void MetricsFiles_OlderThan30Days_AreDeletedAndNewerKept()
    {
        Directory.CreateDirectory(factory.LogsPath);
        var old = CreateDatedFile(factory.LogsPath, daysAgo: 31);
        var boundary = CreateDatedFile(factory.LogsPath, daysAgo: 29);

        factory.Services.GetRequiredService<ResourceSnapshotService>().TakeSnapshot();
        LogManager.Flush();

        Assert.False(File.Exists(old), old);
        Assert.True(File.Exists(boundary), boundary);
        Assert.True(File.Exists(Path.Combine(factory.LogsPath, $"metrics-{DateTime.UtcNow:yyyy-MM-dd}.log")));
    }

    private static string CreateDatedFile(string folder, int daysAgo)
    {
        var date = DateTime.UtcNow.AddDays(-daysAgo);
        var path = Path.Combine(folder, $"metrics-{date:yyyy-MM-dd}.log");
        File.WriteAllText(path, "old\n");
        File.SetCreationTimeUtc(path, date);
        File.SetLastWriteTimeUtc(path, date);
        return path;
    }
}

[Collection(WebHostCollection.Name)]
public class RequestDiagnosticsTests(TempDatabaseWebApplicationFactory factory) : IClassFixture<TempDatabaseWebApplicationFactory>
{
    private sealed class ThrowingStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Run(async _ =>
            {
                await Task.Delay(30);
                throw new InvalidOperationException("diagnostics-test-failure");
            });
        };
    }

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> Host(params (string Key, string Value)[] settings) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Diagnostics:SnapshotIntervalSeconds", "3600");
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        });

    [Fact]
    public async Task SlowRequest_LogsWarningWithoutTheQueryString()
    {
        using var host = Host(("Diagnostics:SlowRequestMilliseconds", "1"));
        using var client = NewClient(host);

        await client.GetAsync("/?secret=abc123");

        var lines = ReadLogLines(factory.LogsPath, "app");
        var slow = lines.First(l => l.Contains("|Diagnostics|Slow request method=GET path=/ status="));
        Assert.Contains("|test-pod|WARN|Diagnostics|Slow request method=GET path=/ status=", slow);
        Assert.Matches(@"status=\d{3} duration=\d+ms$", slow);
        Assert.DoesNotContain(lines, l => l.Contains("secret=abc123"));
    }

    [Fact]
    public async Task ThrowingRequest_IsLoggedAsSlowWithStatus500AndTheExceptionStillPropagatesToTheHandler()
    {
        using var host = Host(
            ("Diagnostics:SlowRequestMilliseconds", "1"));
        using var throwing = host.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddTransient<IStartupFilter, ThrowingStartupFilter>()));
        using var client = NewClient(throwing);

        var response = await client.GetAsync("/diagnostics-throw-check");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains(ReadLogLines(factory.LogsPath, "app"),
            l => l.Contains("|Diagnostics|Slow request method=GET path=/diagnostics-throw-check status=500 duration="));
    }

    [Fact]
    public async Task RateLimited_LogsWarningWithMethodPathAndIp()
    {
        using var client = NewClientFromIp(factory, "10.31.0.1");

        for (var i = 0; i <= AdminLoginRateLimit.PermitLimit; i++)
        {
            await LoginAsync(client, "diag-limited@example.com", "wrong-" + i);
        }

        var line = Assert.Single(
            ReadLogLines(factory.LogsPath, "app"), l => l.Contains("|Diagnostics|Rate limited method=POST path=/admin/login ip=10.31.0.1"));
        Assert.Contains("|test-pod|WARN|Diagnostics|Rate limited method=POST path=/admin/login ip=10.31.0.1", line);
    }

    [Fact]
    public async Task RateLimited_LinesAreCappedPerMinute_ButEveryResponseIsCounted()
    {
        using var host = Host(("Diagnostics:RateLimitedLinesPerMinute", "1"));
        using var client = NewClientFromIp(host, "10.31.0.2");

        for (var i = 0; i < AdminLoginRateLimit.PermitLimit + 2; i++)
        {
            await LoginAsync(client, "diag-capped@example.com", "wrong-" + i);
        }

        Assert.Single(
            ReadLogLines(factory.LogsPath, "app"), l => l.Contains("|Diagnostics|Rate limited method=POST path=/admin/login ip=10.31.0.2"));
        Assert.Equal(2, host.Services.GetRequiredService<RequestCounters>().RateLimited);
    }

    [Fact]
    public void InvalidDiagnosticsOptions_FailStartup()
    {
        using var host = Host(("Diagnostics:SnapshotIntervalSeconds", "0"));

        var ex = Assert.ThrowsAny<Exception>(() => host.CreateClient());

        Assert.Contains("Diagnostics:SnapshotIntervalSeconds must be greater than 0.", ex.ToString());
    }
}
