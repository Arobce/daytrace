using System.Globalization;

namespace DayTrace.Core.Models;

/// <summary>Setting keys as stored in the <c>settings</c> table (PRD §5.1).</summary>
public static class SettingKeys
{
    public const string PollIntervalMs = "poll_interval_ms";
    public const string MinSwitchSeconds = "min_switch_seconds";
    public const string IdleThresholdSeconds = "idle_threshold_seconds";
    public const string HeartbeatSeconds = "heartbeat_seconds";
    public const string DayStartHour = "day_start_hour";
    public const string WebPort = "web_port";
}

/// <summary>Typed view of the <c>settings</c> table. Missing or unparsable values fall back to defaults.</summary>
public sealed record AppSettings(
    int PollIntervalMs,
    int MinSwitchSeconds,
    int IdleThresholdSeconds,
    int HeartbeatSeconds,
    int DayStartHour,
    int WebPort)
{
    public static AppSettings Defaults { get; } = new(
        PollIntervalMs: 1000,
        MinSwitchSeconds: 2,
        IdleThresholdSeconds: 180,
        HeartbeatSeconds: 30,
        DayStartHour: 0,
        WebPort: 5217);

    public IReadOnlyDictionary<string, string> ToDictionary() => new Dictionary<string, string>
    {
        [SettingKeys.PollIntervalMs] = Format(PollIntervalMs),
        [SettingKeys.MinSwitchSeconds] = Format(MinSwitchSeconds),
        [SettingKeys.IdleThresholdSeconds] = Format(IdleThresholdSeconds),
        [SettingKeys.HeartbeatSeconds] = Format(HeartbeatSeconds),
        [SettingKeys.DayStartHour] = Format(DayStartHour),
        [SettingKeys.WebPort] = Format(WebPort),
    };

    public static AppSettings FromDictionary(IReadOnlyDictionary<string, string> values)
    {
        var d = Defaults;
        return new AppSettings(
            Read(values, SettingKeys.PollIntervalMs, d.PollIntervalMs),
            Read(values, SettingKeys.MinSwitchSeconds, d.MinSwitchSeconds),
            Read(values, SettingKeys.IdleThresholdSeconds, d.IdleThresholdSeconds),
            Read(values, SettingKeys.HeartbeatSeconds, d.HeartbeatSeconds),
            Read(values, SettingKeys.DayStartHour, d.DayStartHour),
            Read(values, SettingKeys.WebPort, d.WebPort));
    }

    private static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static int Read(IReadOnlyDictionary<string, string> values, string key, int fallback) =>
        values.TryGetValue(key, out var raw)
        && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
}
