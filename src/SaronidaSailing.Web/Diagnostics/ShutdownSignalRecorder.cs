using System.Runtime.InteropServices;

namespace SaronidaSailing.Web.Diagnostics;

public sealed class ShutdownSignalRecorder : IDisposable
{
    private readonly List<PosixSignalRegistration> _registrations = [];
    private readonly ManualResetEventSlim _recorded = new(false);
    private string? _signalName;

    public ShutdownSignalRecorder(bool register = true)
    {
        if (!register)
        {
            return;
        }

        foreach (var signal in new[] { PosixSignal.SIGTERM, PosixSignal.SIGINT, PosixSignal.SIGQUIT })
        {
            try
            {
                _registrations.Add(PosixSignalRegistration.Create(signal, context => Record(context.Signal)));
            }
            catch (PlatformNotSupportedException)
            {
            }
        }
    }

    public void Record(PosixSignal signal)
    {
        var name = signal switch
        {
            PosixSignal.SIGTERM => "SIGTERM",
            PosixSignal.SIGINT => "SIGINT",
            PosixSignal.SIGQUIT => "SIGQUIT",
            _ => signal.ToString(),
        };

        Interlocked.CompareExchange(ref _signalName, name, null);
        _recorded.Set();
    }

    // The host's own signal handler can run first and call StopApplication before this recorder sees the signal.
    public string GetReason(TimeSpan grace)
    {
        _recorded.Wait(grace);
        return Volatile.Read(ref _signalName) ?? "requested";
    }

    public void Dispose()
    {
        foreach (var registration in _registrations)
        {
            registration.Dispose();
        }

        _registrations.Clear();
    }
}
