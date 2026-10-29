using Dapper;
using DayTrace.Core.Models;
using Microsoft.Data.Sqlite;

namespace DayTrace.Core.Data.Migrations;

/// <summary>First-run categories, rules and settings (PRD §5.1, §5.3). Runs once, so later user edits are never overwritten.</summary>
internal sealed class M002_SeedData : IMigration
{
    public const string UncategorizedName = "Uncategorized";
    public const int TitleRulePriority = 10;

    private const string InsertCategorySql = """
        INSERT INTO categories (name, color, productivity, sort_order, is_system)
        VALUES (@Name, @Color, @Productivity, @SortOrder, @IsSystem)
        RETURNING id
        """;

    private const string InsertRuleSql = """
        INSERT INTO rules (category_id, match_type, pattern, exe_filter, priority, created_at)
        VALUES (@CategoryId, @MatchType, @Pattern, NULL, @Priority, @CreatedAt)
        """;

    private const string InsertSettingSql = "INSERT INTO settings (key, value) VALUES (@Key, @Value)";

    internal sealed record SeedCategory(
        string Name,
        string Color,
        Productivity Productivity,
        string[] Exes,
        string[] Titles,
        bool IsSystem = false);

    internal static readonly SeedCategory[] Categories =
    [
        new("Productive", "#2F9E6E", Productivity.Productive,
            ["code.exe", "devenv.exe", "rider64.exe", "idea64.exe", "windowsterminal.exe", "powershell.exe",
             "pwsh.exe", "cmd.exe", "winword.exe", "excel.exe", "powerpnt.exe", "onenote.exe", "notion.exe",
             "obsidian.exe", "figma.exe", "claude.exe"],
            ["GitHub", "Stack Overflow", "Google Docs", "Google Sheets"]),
        new("Communication", "#3B7DD8", Productivity.Neutral,
            ["slack.exe", "ms-teams.exe", "teams.exe", "outlook.exe", "olk.exe", "zoom.exe", "discord.exe"],
            ["Gmail", "Outlook", "WhatsApp"]),
        new("Browsing", "#8A7FD1", Productivity.Neutral,
            ["chrome.exe", "msedge.exe", "firefox.exe", "brave.exe", "opera.exe", "arc.exe"],
            []),
        new("Entertainment", "#E0654F", Productivity.Distracting,
            ["spotify.exe", "steam.exe", "vlc.exe"],
            ["YouTube", "Netflix", "Twitch", "Prime Video", "Disney+"]),
        new("Social", "#E39B3A", Productivity.Distracting,
            [],
            ["Reddit", " / X", "Facebook", "Instagram", "LinkedIn"]),
        new("System", "#9AA1AC", Productivity.Neutral,
            ["explorer.exe", "systemsettings.exe", "taskmgr.exe", "searchhost.exe", "shellexperiencehost.exe",
             "startmenuexperiencehost.exe"],
            []),
        new(UncategorizedName, "#C8CDD4", Productivity.Neutral, [], [], IsSystem: true),
    ];

    public int Version => 2;

    public void Apply(SqliteConnection connection, SqliteTransaction tx, IClock clock)
    {
        var now = clock.UtcNowMs;

        for (var i = 0; i < Categories.Length; i++)
        {
            var seed = Categories[i];
            var categoryId = connection.ExecuteScalar<long>(InsertCategorySql, new
            {
                seed.Name,
                seed.Color,
                Productivity = DbText.From(seed.Productivity),
                SortOrder = i,
                IsSystem = seed.IsSystem ? 1 : 0,
            }, tx);

            var rules = seed.Exes
                .Select(exe => new { CategoryId = categoryId, MatchType = DbText.From(RuleMatchType.Exe), Pattern = exe, Priority = 0, CreatedAt = now })
                .Concat(seed.Titles.Select(title => new
                {
                    CategoryId = categoryId,
                    MatchType = DbText.From(RuleMatchType.TitleContains),
                    Pattern = title,
                    Priority = TitleRulePriority,
                    CreatedAt = now,
                }));
            connection.Execute(InsertRuleSql, rules, tx);
        }

        var settings = AppSettings.Defaults.ToDictionary().Select(kv => new { kv.Key, kv.Value });
        connection.Execute(InsertSettingSql, settings, tx);
    }
}
