using NLog;

namespace SaronidaSailing.Web.Diagnostics;

public static class CrashLogging
{
    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(5);

    private static int _registered;

    public static void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1)
        {
            return;
        }

        var logger = LogManager.GetLogger(LifecycleLogger.LoggerName);
        AppDomain.CurrentDomain.UnhandledException += (sender, e) => OnUnhandledException(logger, e);
        TaskScheduler.UnobservedTaskException += (sender, e) => OnUnobservedTaskException(logger, e);
    }

    public static void OnUnhandledException(Logger logger, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            logger.Fatal(exception, "Unhandled exception terminating={0}", e.IsTerminating);
        }
        else
        {
            logger.Fatal("Unhandled exception terminating={0} object={1}", e.IsTerminating, e.ExceptionObject);
        }

        logger.Factory.Flush(FlushTimeout);
    }

    public static void OnUnobservedTaskException(Logger logger, UnobservedTaskExceptionEventArgs e) =>
        logger.Error(e.Exception, "Unobserved task exception");

    public static void LogTermination(Exception exception, bool hasStarted) =>
        LogTermination(LogManager.GetLogger(LifecycleLogger.LoggerName), exception, hasStarted);

    public static void LogTermination(Logger logger, Exception exception, bool hasStarted)
    {
        if (exception is OperationCanceledException or HostAbortedException)
        {
            return;
        }

        logger.Fatal(exception, hasStarted ? "Host terminated unexpectedly" : "Startup failed");
        logger.Factory.Flush(FlushTimeout);
    }
}
