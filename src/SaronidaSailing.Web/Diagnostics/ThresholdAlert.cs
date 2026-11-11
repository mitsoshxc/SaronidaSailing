namespace SaronidaSailing.Web.Diagnostics;

public enum AlertChange
{
    None,
    Started,
    Repeated,
    Cleared,
}

public sealed class ThresholdAlert
{
    private DateTimeOffset _lastLoggedAt;
    private bool _active;

    public AlertChange Evaluate(bool breached, DateTimeOffset now, TimeSpan repeatEvery)
    {
        if (!breached)
        {
            var wasActive = _active;
            _active = false;
            return wasActive ? AlertChange.Cleared : AlertChange.None;
        }

        if (!_active)
        {
            _active = true;
            _lastLoggedAt = now;
            return AlertChange.Started;
        }

        if (now - _lastLoggedAt < repeatEvery)
        {
            return AlertChange.None;
        }

        _lastLoggedAt = now;
        return AlertChange.Repeated;
    }
}
