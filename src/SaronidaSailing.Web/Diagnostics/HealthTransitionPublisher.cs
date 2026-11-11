using Microsoft.Extensions.Diagnostics.HealthChecks;
using NLog;

namespace SaronidaSailing.Web.Diagnostics;

public sealed class HealthTransitionPublisher(Logger logger) : IHealthCheckPublisher
{
    public const string OverallName = "overall";

    private readonly object _lock = new();
    private readonly Dictionary<string, HealthStatus> _last = [];

    private bool _hasReported;

    public Task PublishAsync(HealthReport report, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            var first = !_hasReported;
            _hasReported = true;

            foreach (var (name, entry) in report.Entries)
            {
                Track(name, entry.Status, entry.Description, entry.Exception, first, logAtStartup: true);
            }

            var failing = string.Join(", ", report.Entries.Where(e => e.Value.Status != HealthStatus.Healthy).Select(e => e.Key));
            Track(OverallName, report.Status, failing.Length == 0 ? null : $"failing: {failing}", null, first, logAtStartup: false);
        }

        return Task.CompletedTask;
    }

    private void Track(string name, HealthStatus status, string? description, Exception? exception, bool first, bool logAtStartup)
    {
        _last.TryGetValue(name, out var previous);
        _last[name] = status;
        var detail = string.IsNullOrWhiteSpace(description) ? "no description" : description;

        if (first)
        {
            if (logAtStartup && status != HealthStatus.Healthy)
            {
                logger.Warn(exception, "{0}", $"Health check {name} {status} at startup: {detail}");
            }

            return;
        }

        if (status == previous)
        {
            return;
        }

        if (status == HealthStatus.Healthy)
        {
            logger.Info("{0}", $"Health check {name} recovered: {previous} -> {status}");
        }
        else
        {
            logger.Warn(exception, "{0}", $"Health check {name} {previous} -> {status}: {detail}");
        }
    }
}
