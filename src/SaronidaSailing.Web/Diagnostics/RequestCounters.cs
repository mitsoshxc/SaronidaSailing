namespace SaronidaSailing.Web.Diagnostics;

public readonly record struct RequestTotals(long Completed, long Errors5xx, long RateLimited);

public sealed class RequestCounters
{
    private readonly object _lineLock = new();

    private long _inFlight;
    private long _completed;
    private long _errors5xx;
    private long _rateLimited;
    private long _lineMinute = -1;
    private int _lineCount;

    public long InFlight => Interlocked.Read(ref _inFlight);

    public long RateLimited => Interlocked.Read(ref _rateLimited);

    public void RequestStarted() => Interlocked.Increment(ref _inFlight);

    public void RequestCompleted(int statusCode)
    {
        Interlocked.Decrement(ref _inFlight);
        Interlocked.Increment(ref _completed);
        if (statusCode >= 500)
        {
            Interlocked.Increment(ref _errors5xx);
        }
        else if (statusCode == StatusCodes.Status429TooManyRequests)
        {
            Interlocked.Increment(ref _rateLimited);
        }
    }

    public RequestTotals TakeTotals() => new(
        Interlocked.Exchange(ref _completed, 0),
        Interlocked.Exchange(ref _errors5xx, 0),
        Interlocked.Exchange(ref _rateLimited, 0));

    public bool TryTakeRateLimitedLine(DateTimeOffset now, int linesPerMinute)
    {
        var minute = now.UtcTicks / TimeSpan.TicksPerMinute;
        lock (_lineLock)
        {
            if (minute != _lineMinute)
            {
                _lineMinute = minute;
                _lineCount = 0;
            }

            if (_lineCount >= linesPerMinute)
            {
                return false;
            }

            _lineCount++;
            return true;
        }
    }
}
