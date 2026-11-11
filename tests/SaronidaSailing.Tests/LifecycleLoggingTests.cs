using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using NLog;
using NLog.Config;
using NLog.Targets;
using SaronidaSailing.Web.Diagnostics;

namespace SaronidaSailing.Tests;

[Collection(WebHostCollection.Name)]
public class LifecycleLoggingTests(TempDatabaseWebApplicationFactory factory) : IClassFixture<TempDatabaseWebApplicationFactory>
{
    [Fact]
    public async Task WebHost_LogsExactlyOneStartedLine()
    {
        _ = factory.Services;

        var lines = await WaitForLinesAsync(factory.LogsPath, l => l.Contains("|Lifecycle|Started "));

        var started = Assert.Single(lines, l => l.Contains("|Lifecycle|Started "));
        Assert.Contains("|test-pod|INFO|Lifecycle|Started version=", started);
        Assert.Contains(" environment=Development ", started);
        Assert.Matches(@" dotnet=\.NET \S+ ", started);
        Assert.Matches(@" cpus=\d+ ", started);
        Assert.Matches(@" memoryLimit=\d+MiB gcHeapLimit=\d+MiB ", started);
        Assert.Matches(@" pid=\d+$", started);
    }

    [Fact]
    public async Task StoppingTheHost_LogsStoppingThenStoppedAfterStarted()
    {
        using var host = new TempDatabaseWebApplicationFactory();
        host.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();

        var lines = await WaitForLinesAsync(host.LogsPath, l => l.Contains("|Lifecycle|Stopped "));

        var lifecycle = lines.Where(l => Regex.IsMatch(l, @"\|INFO\|Lifecycle\|(Started|Stopping|Stopped) ")).ToList();
        Assert.True(lifecycle.Count == 3, string.Join("\n", lines));
        Assert.Contains("|Lifecycle|Started ", lifecycle[0]);
        Assert.Matches(@"\|test-pod\|INFO\|Lifecycle\|Stopping reason=requested uptime=\d{2}:\d{2}:\d{2}$", lifecycle[1]);
        Assert.Matches(@"\|test-pod\|INFO\|Lifecycle\|Stopped uptime=\d{2}:\d{2}:\d{2}$", lifecycle[2]);
    }

    [Fact]
    public async Task MigrateCommand_WritesNoLifecycleLines()
    {
        var connectionString = await PostgresTestServer.CreateEmptyDatabaseAsync();
        var directory = Path.Combine(Path.GetTempPath(), "saronida-cli-" + Guid.NewGuid().ToString("N"));
        try
        {
            var dll = typeof(Program).Assembly.Location;
            var info = new ProcessStartInfo("dotnet", $"\"{dll}\" migrate")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(dll)!,
            };
            info.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
            info.Environment["Logs__Path"] = Path.Combine(directory, "logs");
            info.Environment["Logs__Instance"] = "test-pod";
            info.Environment["ConnectionStrings__Content"] = connectionString;
            info.Environment["DataProtection__KeysPath"] = Path.Combine(directory, "keys");
            info.Environment["Media__RootPath"] = Path.Combine(directory, "media");
            info.Environment["Media__OriginalsPath"] = Path.Combine(directory, "originals");
            info.Environment["Site__BaseUrl"] = "https://localhost";

            using var process = Process.Start(info)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await process.WaitForExitAsync(timeout.Token);

            Assert.True(process.ExitCode == 0, await error);
            Assert.Contains("Hangfire schema up to date.", await output);
            foreach (var word in new[] { "Started", "Stopping", "Stopped" })
            {
                Assert.DoesNotContain("|Lifecycle|" + word, await output);
            }

            var logs = Path.Combine(directory, "logs");
            var logged = Directory.Exists(logs)
                ? Directory.GetFiles(logs).SelectMany(f => ReadLines(Path.GetDirectoryName(f)!, Path.GetFileName(f)))
                : [];
            Assert.DoesNotContain(logged, l => l.Contains("|Lifecycle|"));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await PostgresTestServer.DropDatabaseAsync(PostgresTestServer.DatabaseName(connectionString));
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task StartupFailure_LogsFatalStartupFailedWithTheExceptionAndStillThrows()
    {
        using var failing = new ProductionModeWebApplicationFactory();

        Assert.ThrowsAny<Exception>(() => failing.CreateClient());

        var lines = await WaitForLinesAsync(failing.LogsPath, l => l.Contains("|Lifecycle|Startup failed"));
        var fatal = Assert.Single(lines, l => l.Contains("|FATAL|Lifecycle|Startup failed"));
        Assert.Contains("|test-pod|FATAL|Lifecycle|Startup failed", fatal);
        Assert.Contains(lines, l => l.Contains("migrate") && l.Contains("_InitialCreate"));
        Assert.DoesNotContain(lines, l => l.Contains("|Lifecycle|Started "));
    }

    private static async Task<string[]> WaitForLinesAsync(string logsPath, Func<string, bool> until)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (true)
        {
            LogManager.Flush();
            var lines = ReadLines(logsPath, $"app-{DateTime.UtcNow:yyyy-MM-dd}.log");
            if (lines.Any(until) || DateTime.UtcNow > deadline)
            {
                return lines;
            }

            await Task.Delay(100);
        }
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

public class LifecycleLoggerUnitTests
{
    private sealed class CapturedLog : IDisposable
    {
        private readonly LogFactory _factory = new();
        private readonly MemoryTarget _target = new("memory")
        {
            Layout = "${level:uppercase=true}|${logger}|${message}${onexception:inner=|${exception:format=message}}",
        };

        public CapturedLog()
        {
            var configuration = new LoggingConfiguration(_factory);
            configuration.AddRule(LogLevel.Trace, LogLevel.Fatal, _target);
            _factory.Configuration = configuration;
            Logger = _factory.GetLogger(LifecycleLogger.LoggerName);
        }

        public Logger Logger { get; }

        public IList<string> Lines => _target.Logs;

        public void Dispose() => _factory.Dispose();
    }

    [Fact]
    public void ShutdownSignalRecorder_ReportsRecordedSignalName_OtherwiseRequested()
    {
        using var none = new ShutdownSignalRecorder(register: false);
        using var term = new ShutdownSignalRecorder(register: false);
        using var interrupt = new ShutdownSignalRecorder(register: false);
        using var quit = new ShutdownSignalRecorder(register: false);

        term.Record(PosixSignal.SIGTERM);
        term.Record(PosixSignal.SIGINT);
        interrupt.Record(PosixSignal.SIGINT);
        quit.Record(PosixSignal.SIGQUIT);

        Assert.Equal("requested", none.GetReason(TimeSpan.Zero));
        Assert.Equal("SIGTERM", term.GetReason(TimeSpan.Zero));
        Assert.Equal("SIGINT", interrupt.GetReason(TimeSpan.Zero));
        Assert.Equal("SIGQUIT", quit.GetReason(TimeSpan.Zero));
    }

    [Fact]
    public void LifecycleLogger_WithRecordedSignal_LogsReasonAndUptimeFromTimeProvider()
    {
        using var log = new CapturedLog();
        var time = new FakeTimeProvider();
        using var signals = new ShutdownSignalRecorder(register: false);
        var lifecycle = new LifecycleLogger(log.Logger, time, signals);

        Assert.False(lifecycle.HasStarted);
        lifecycle.LogStarted("1.2.3", "Production", ".NET 10.0.0", 2, 512L * 1024 * 1024, 384L * 1024 * 1024, 42);
        time.Advance(TimeSpan.FromHours(25) + TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(3));
        signals.Record(PosixSignal.SIGTERM);
        lifecycle.LogStopping();
        lifecycle.LogStopped();

        Assert.True(lifecycle.HasStarted);
        Assert.Equal(
            [
                "INFO|Lifecycle|Started version=1.2.3 environment=Production dotnet=.NET 10.0.0 cpus=2 memoryLimit=512MiB gcHeapLimit=384MiB pid=42",
                "INFO|Lifecycle|Stopping reason=SIGTERM uptime=25:02:03",
                "INFO|Lifecycle|Stopped uptime=25:02:03",
            ],
            log.Lines);
    }

    [Fact]
    public void LifecycleLogger_WithoutSignal_LogsRequested()
    {
        using var log = new CapturedLog();
        using var signals = new ShutdownSignalRecorder(register: false);
        var lifecycle = new LifecycleLogger(log.Logger, new FakeTimeProvider(), signals);

        lifecycle.LogStopping();

        Assert.Equal(["INFO|Lifecycle|Stopping reason=requested uptime=00:00:00"], log.Lines);
    }

    [Fact]
    public void ResolveVersion_UsesInformationalVersionOfTheAssembly()
    {
        Assert.NotEqual("unknown", LifecycleLogger.ResolveVersion(typeof(Program).Assembly));
    }

    [Fact]
    public void UnhandledException_LogsFatalWithTerminatingFlagAndException()
    {
        using var log = new CapturedLog();

        CrashLogging.OnUnhandledException(log.Logger, new UnhandledExceptionEventArgs(new InvalidOperationException("boom"), true));

        Assert.Equal(["FATAL|Lifecycle|Unhandled exception terminating=True|boom"], log.Lines);
    }

    [Fact]
    public void UnobservedTaskException_LogsErrorNotFatal()
    {
        using var log = new CapturedLog();

        CrashLogging.OnUnobservedTaskException(
            log.Logger, new UnobservedTaskExceptionEventArgs(new AggregateException(new InvalidOperationException("lost"))));

        var line = Assert.Single(log.Lines);
        Assert.StartsWith("ERROR|Lifecycle|Unobserved task exception|", line);
        Assert.Contains("lost", line);
    }

    [Fact]
    public void LogTermination_DistinguishesStartupFromRunningAndIgnoresShutdownCancellation()
    {
        using var log = new CapturedLog();

        CrashLogging.LogTermination(log.Logger, new InvalidOperationException("a"), hasStarted: false);
        CrashLogging.LogTermination(log.Logger, new InvalidOperationException("b"), hasStarted: true);
        CrashLogging.LogTermination(log.Logger, new OperationCanceledException(), hasStarted: true);

        Assert.Equal(
            ["FATAL|Lifecycle|Startup failed|a", "FATAL|Lifecycle|Host terminated unexpectedly|b"],
            log.Lines);
    }
}
