using DayTrace.Core.Models;

namespace DayTrace.Core.Data;

/// <summary>Maps enums to and from the TEXT values allowed by the schema's CHECK constraints.</summary>
internal static class DbText
{
    public static string From(AwayReason reason) => reason switch
    {
        AwayReason.Idle => "idle",
        AwayReason.Locked => "locked",
        AwayReason.Asleep => "asleep",
        AwayReason.Paused => "paused",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };

    public static AwayReason ToAwayReason(string value) => value switch
    {
        "idle" => AwayReason.Idle,
        "locked" => AwayReason.Locked,
        "asleep" => AwayReason.Asleep,
        "paused" => AwayReason.Paused,
        _ => throw new FormatException($"Unknown away reason '{value}'."),
    };

    public static string From(Productivity productivity) => productivity switch
    {
        Productivity.Productive => "productive",
        Productivity.Neutral => "neutral",
        Productivity.Distracting => "distracting",
        _ => throw new ArgumentOutOfRangeException(nameof(productivity), productivity, null),
    };

    public static Productivity ToProductivity(string value) => value switch
    {
        "productive" => Productivity.Productive,
        "neutral" => Productivity.Neutral,
        "distracting" => Productivity.Distracting,
        _ => throw new FormatException($"Unknown productivity '{value}'."),
    };

    public static string From(RuleMatchType matchType) => matchType switch
    {
        RuleMatchType.Exe => "exe",
        RuleMatchType.TitleContains => "title_contains",
        _ => throw new ArgumentOutOfRangeException(nameof(matchType), matchType, null),
    };

    public static RuleMatchType ToMatchType(string value) => value switch
    {
        "exe" => RuleMatchType.Exe,
        "title_contains" => RuleMatchType.TitleContains,
        _ => throw new FormatException($"Unknown match type '{value}'."),
    };
}
