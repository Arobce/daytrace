namespace DayTrace.Core.Models;

public enum AwayReason
{
    Idle,
    Locked,
    Asleep,
    Paused,
}

/// <summary>A stretch of time excluded from active time. Times are UTC ms.</summary>
public sealed record AwayPeriod(long Id, long StartedAt, long EndedAt, AwayReason Reason);
