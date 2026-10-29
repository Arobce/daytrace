namespace DayTrace.Core.Tests;

public sealed class FakeClock(long utcNowMs = FakeClock.DefaultStart) : IClock
{
    /// <summary>2026-10-28T08:00:00Z.</summary>
    public const long DefaultStart = 1_793_174_400_000;

    public long UtcNowMs { get; set; } = utcNowMs;

    public void Advance(TimeSpan by) => UtcNowMs += (long)by.TotalMilliseconds;
}
