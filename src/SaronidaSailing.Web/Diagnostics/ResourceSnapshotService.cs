using System.Globalization;
using Microsoft.Extensions.Options;
using NLog;

namespace SaronidaSailing.Web.Diagnostics;

public sealed class ResourceSnapshotService(
    IProcessReadings readings,
    RequestCounters counters,
    TimeProvider time,
    IOptions<DiagnosticsOptions> options,
    Logger metrics,
    Logger diagnostics) : BackgroundService
{
    public const string MetricsLoggerName = "Metrics";

    private const double BytesPerMiB = 1024.0 * 1024.0;

    private readonly ThresholdAlert _memoryAlert = new();
    private readonly ThresholdAlert _queueAlert = new();

    private DateTimeOffset? _lastAt;
    private TimeSpan _lastCpu;

    public static string BuildMessage(
        ProcessSample sample, double memoryPercent, double cpuPercent, long inFlight, RequestTotals totals) =>
        string.Create(CultureInfo.InvariantCulture,
            $"workingSetMiB={ToMiB(sample.WorkingSetBytes)} gcHeapMiB={ToMiB(sample.GcHeapBytes)} memoryLimitMiB={ToMiB(sample.MemoryLimitBytes)} "
            + $"gcHeapLimitMiB={ToMiB(sample.GcHeapLimitBytes)} memoryPct={memoryPercent:F1} cpuPct={cpuPercent:F1} gc0={sample.Gc0} gc1={sample.Gc1} gc2={sample.Gc2} "
            + $"threads={sample.Threads} queue={sample.QueueLength} inFlight={inFlight} requestsPerMin={totals.Completed} "
            + $"errors5xxPerMin={totals.Errors5xx} rateLimitedPerMin={totals.RateLimited}");

    public void TakeSnapshot()
    {
        try
        {
            Capture();
        }
        catch (Exception ex)
        {
            diagnostics.Warn(ex, "Resource snapshot failed");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.SnapshotIntervalSeconds), time);
        try
        {
            SetBaseline();
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                TakeSnapshot();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private void SetBaseline()
    {
        try
        {
            _lastCpu = readings.Read().CpuTime;
            _lastAt = time.GetUtcNow();
        }
        catch (Exception ex)
        {
            diagnostics.Warn(ex, "Resource snapshot baseline failed");
        }
    }

    private void Capture()
    {
        var sample = readings.Read();
        var now = time.GetUtcNow();
        var cpuPercent = 0.0;
        if (_lastAt is { } last && now > last && sample.Cpus > 0)
        {
            cpuPercent = (sample.CpuTime - _lastCpu).TotalSeconds / ((now - last).TotalSeconds * sample.Cpus) * 100;
        }

        _lastAt = now;
        _lastCpu = sample.CpuTime;

        var memoryPercent = sample.MemoryLimitBytes > 0 ? (double)sample.WorkingSetBytes / sample.MemoryLimitBytes * 100 : 0;
        metrics.Info("{0}", BuildMessage(sample, memoryPercent, Math.Max(cpuPercent, 0), counters.InFlight, counters.TakeTotals()));
        EvaluateAlerts(sample, memoryPercent, now);
    }

    private void EvaluateAlerts(ProcessSample sample, double memoryPercent, DateTimeOffset now)
    {
        var repeat = TimeSpan.FromMinutes(options.Value.WarningRepeatMinutes);

        var memoryDetail = string.Create(CultureInfo.InvariantCulture,
            $"memoryPct={memoryPercent:F1} workingSetMiB={ToMiB(sample.WorkingSetBytes)} memoryLimitMiB={ToMiB(sample.MemoryLimitBytes)}");
        Report(_memoryAlert.Evaluate(memoryPercent > options.Value.MemoryWarningPercent, now, repeat),
            "Memory high", "Memory back to normal", memoryDetail);

        var queueDetail = string.Create(CultureInfo.InvariantCulture, $"queue={sample.QueueLength} threads={sample.Threads}");
        Report(_queueAlert.Evaluate(sample.QueueLength >= options.Value.ThreadPoolQueueWarning, now, repeat),
            "Thread pool queue high", "Thread pool queue back to normal", queueDetail);
    }

    private void Report(AlertChange change, string warning, string recovered, string detail)
    {
        switch (change)
        {
            case AlertChange.Started or AlertChange.Repeated:
                diagnostics.Warn("{0} {1}", warning, detail);
                break;
            case AlertChange.Cleared:
                diagnostics.Info("{0} {1}", recovered, detail);
                break;
        }
    }

    private static long ToMiB(long bytes) => (long)(bytes / BytesPerMiB);
}
