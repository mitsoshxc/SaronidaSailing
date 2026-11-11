using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using NLog;

namespace SaronidaSailing.Web.Diagnostics;

public sealed class LifecycleLogger(Logger logger, TimeProvider time, ShutdownSignalRecorder signals)
{
    public const string LoggerName = "Lifecycle";

    private static readonly TimeSpan SignalGrace = TimeSpan.FromMilliseconds(250);

    private long _startedAtTicks;

    public bool HasStarted => Volatile.Read(ref _startedAtTicks) != 0;

    public static LifecycleLogger Attach(IHostApplicationLifetime lifetime, IHostEnvironment environment, TimeProvider time, Assembly assembly)
    {
        var lifecycle = new LifecycleLogger(LogManager.GetLogger(LoggerName), time, new ShutdownSignalRecorder());
        lifetime.ApplicationStarted.Register(() => lifecycle.LogStarted(
            ResolveVersion(assembly), environment.EnvironmentName, RuntimeInformation.FrameworkDescription,
            Environment.ProcessorCount, ContainerMemoryLimit.Bytes, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes, Environment.ProcessId));
        lifetime.ApplicationStopping.Register(lifecycle.LogStopping);
        lifetime.ApplicationStopped.Register(lifecycle.LogStopped);
        return lifecycle;
    }

    public static string ResolveVersion(Assembly assembly) =>
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion is { Length: > 0 } version
            ? version
            : "unknown";

    public static string FormatUptime(TimeSpan uptime)
    {
        var value = uptime < TimeSpan.Zero ? TimeSpan.Zero : uptime;
        return string.Create(CultureInfo.InvariantCulture, $"{(long)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}");
    }

    public static string StartedMessage(string version, string environment, string dotnet, int cpus, long memoryLimitBytes, long gcHeapLimitBytes, int pid) =>
        string.Create(CultureInfo.InvariantCulture,
            $"Started version={version} environment={environment} dotnet={dotnet} cpus={cpus} memoryLimit={memoryLimitBytes / 1024 / 1024}MiB gcHeapLimit={gcHeapLimitBytes / 1024 / 1024}MiB pid={pid}");

    public void LogStarted(string version, string environment, string dotnet, int cpus, long memoryLimitBytes, long gcHeapLimitBytes, int pid)
    {
        Volatile.Write(ref _startedAtTicks, time.GetUtcNow().UtcTicks);
        logger.Info(StartedMessage(version, environment, dotnet, cpus, memoryLimitBytes, gcHeapLimitBytes, pid));
    }

    public void LogStopping() =>
        logger.Info(string.Create(CultureInfo.InvariantCulture,
            $"Stopping reason={signals.GetReason(SignalGrace)} uptime={FormatUptime(Uptime())}"));

    public void LogStopped()
    {
        logger.Info(string.Create(CultureInfo.InvariantCulture, $"Stopped uptime={FormatUptime(Uptime())}"));
        logger.Factory.Flush(TimeSpan.FromSeconds(5));
        signals.Dispose();
    }

    private TimeSpan Uptime() =>
        Volatile.Read(ref _startedAtTicks) is var ticks and not 0 ? time.GetUtcNow() - new DateTimeOffset(ticks, TimeSpan.Zero) : TimeSpan.Zero;
}
