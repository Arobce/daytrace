namespace DayTrace.Core.Models;

/// <summary>One continuous stretch of a foreground window. Times are UTC ms.</summary>
public sealed record Session(
    long Id,
    long StartedAt,
    long EndedAt,
    string ExeName,
    string? ExePath,
    string? AppName,
    string WindowTitle);
