namespace DayTrace.Core;

/// <summary>Source of "now" for Core. All values are UTC Unix milliseconds.</summary>
public interface IClock
{
    long UtcNowMs { get; }
}

public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();

    private SystemClock() { }

    public long UtcNowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
