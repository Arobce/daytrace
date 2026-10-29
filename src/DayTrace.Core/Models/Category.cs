namespace DayTrace.Core.Models;

public enum Productivity
{
    Productive,
    Neutral,
    Distracting,
}

/// <param name="Color">'#RRGGBB'.</param>
/// <param name="IsSystem">True for 'Uncategorized', which cannot be deleted or renamed.</param>
public sealed record Category(
    long Id,
    string Name,
    string Color,
    Productivity Productivity,
    int SortOrder,
    bool IsSystem);
