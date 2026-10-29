namespace DayTrace.Core.Models;

public enum RuleMatchType
{
    Exe,
    TitleContains,
}

/// <param name="Pattern">Exe rules: lower-case exe name. Title rules: case-insensitive substring.</param>
/// <param name="ExeFilter">Title rules only: restrict the rule to this exe.</param>
/// <param name="Priority">Higher wins among rules of the same match type.</param>
public sealed record Rule(
    long Id,
    long CategoryId,
    RuleMatchType MatchType,
    string Pattern,
    string? ExeFilter,
    int Priority,
    long CreatedAt);
