using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NLog;

namespace SaronidaSailing.Web.Diagnostics;

public static class DiagnosticsSetup
{
    public static IServiceCollection AddRuntimeDiagnostics(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DiagnosticsOptions>()
            .Bind(configuration.GetSection(DiagnosticsOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<DiagnosticsOptions>, DiagnosticsOptionsValidator>();

        services.AddSingleton<RequestCounters>();
        services.AddSingleton<IProcessReadings, ProcessReadings>();
        services.AddSingleton(sp => new ResourceSnapshotService(
            sp.GetRequiredService<IProcessReadings>(),
            sp.GetRequiredService<RequestCounters>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<IOptions<DiagnosticsOptions>>(),
            LogManager.GetLogger(ResourceSnapshotService.MetricsLoggerName),
            LogManager.GetLogger(RequestDiagnosticsMiddleware.LoggerName)));
        services.AddHostedService(sp => sp.GetRequiredService<ResourceSnapshotService>());

        services.AddSingleton<IHealthCheckPublisher>(
            _ => new HealthTransitionPublisher(LogManager.GetLogger(RequestDiagnosticsMiddleware.LoggerName)));
        services.AddOptions<HealthCheckPublisherOptions>()
            .Configure<IOptions<DiagnosticsOptions>>((publisher, diagnostics) =>
            {
                publisher.Delay = TimeSpan.FromSeconds(5);
                publisher.Period = TimeSpan.FromSeconds(Math.Max(diagnostics.Value.HealthCheckPeriodSeconds, 1));
            });

        return services;
    }

    public static IApplicationBuilder UseRequestDiagnostics(this IApplicationBuilder app) =>
        app.UseMiddleware<RequestDiagnosticsMiddleware>();
}
