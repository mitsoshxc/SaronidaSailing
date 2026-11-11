using Microsoft.Extensions.Options;

namespace SaronidaSailing.Web.Diagnostics;

public sealed class DiagnosticsOptions
{
    public const string SectionName = "Diagnostics";

    public int SnapshotIntervalSeconds { get; set; } = 60;

    public int SlowRequestMilliseconds { get; set; } = 2000;

    public int MemoryWarningPercent { get; set; } = 80;

    public int ThreadPoolQueueWarning { get; set; } = 100;

    public int WarningRepeatMinutes { get; set; } = 10;

    public int RateLimitedLinesPerMinute { get; set; } = 20;

    public int HealthCheckPeriodSeconds { get; set; } = 30;
}

public sealed class DiagnosticsOptionsValidator : IValidateOptions<DiagnosticsOptions>
{
    public ValidateOptionsResult Validate(string? name, DiagnosticsOptions options)
    {
        var failures = new List<string>();
        Require(failures, nameof(options.SnapshotIntervalSeconds), options.SnapshotIntervalSeconds);
        Require(failures, nameof(options.SlowRequestMilliseconds), options.SlowRequestMilliseconds);
        Require(failures, nameof(options.MemoryWarningPercent), options.MemoryWarningPercent);
        Require(failures, nameof(options.ThreadPoolQueueWarning), options.ThreadPoolQueueWarning);
        Require(failures, nameof(options.WarningRepeatMinutes), options.WarningRepeatMinutes);
        Require(failures, nameof(options.RateLimitedLinesPerMinute), options.RateLimitedLinesPerMinute);
        Require(failures, nameof(options.HealthCheckPeriodSeconds), options.HealthCheckPeriodSeconds);
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void Require(List<string> failures, string property, int value)
    {
        if (value <= 0)
        {
            failures.Add($"{DiagnosticsOptions.SectionName}:{property} must be greater than 0.");
        }
    }
}
