using DayTrace.Core.Data.Repositories;
using DayTrace.Core.Models;
using Microsoft.Data.Sqlite;

namespace DayTrace.Core.Tests;

/// <summary>First-run data must match PRD §5.1 and §5.3.</summary>
public sealed class SeedDataTests : IDisposable
{
    private readonly TempDatabase _db = new();
    private readonly FakeClock _clock = new();
    private readonly SqliteConnection _connection;

    public SeedDataTests()
    {
        _connection = _db.OpenMigrated(_clock);
    }

    public void Dispose()
    {
        _connection.Dispose();
        _db.Dispose();
    }

    [Fact]
    public void Seeds_the_seven_categories_in_order()
    {
        var categories = new CategoryRepository(_connection).GetAll();

        var actual = categories.Select(c => (c.Name, c.Color, c.Productivity, c.IsSystem)).ToArray();
        (string, string, Productivity, bool)[] expected =
        [
            ("Productive", "#2F9E6E", Productivity.Productive, false),
            ("Communication", "#3B7DD8", Productivity.Neutral, false),
            ("Browsing", "#8A7FD1", Productivity.Neutral, false),
            ("Entertainment", "#E0654F", Productivity.Distracting, false),
            ("Social", "#E39B3A", Productivity.Distracting, false),
            ("System", "#9AA1AC", Productivity.Neutral, false),
            ("Uncategorized", "#C8CDD4", Productivity.Neutral, true),
        ];
        Assert.Equal(expected, actual);
        Assert.Equal(Enumerable.Range(0, 7), categories.Select(c => c.SortOrder));
    }

    [Fact]
    public void Uncategorized_is_the_system_category()
    {
        var uncategorized = new CategoryRepository(_connection).GetUncategorized();

        Assert.Equal("Uncategorized", uncategorized.Name);
        Assert.True(uncategorized.IsSystem);
    }

    [Theory]
    [InlineData("Productive", 16)]
    [InlineData("Communication", 7)]
    [InlineData("Browsing", 6)]
    [InlineData("Entertainment", 3)]
    [InlineData("Social", 0)]
    [InlineData("System", 6)]
    [InlineData("Uncategorized", 0)]
    public void Seeds_exe_rules_per_category(string category, int expectedCount)
    {
        var exeRules = RulesFor(category).Where(r => r.MatchType == RuleMatchType.Exe).ToList();

        Assert.Equal(expectedCount, exeRules.Count);
        Assert.All(exeRules, r =>
        {
            Assert.Equal(r.Pattern.ToLowerInvariant(), r.Pattern);
            Assert.EndsWith(".exe", r.Pattern);
            Assert.Null(r.ExeFilter);
            Assert.Equal(0, r.Priority);
        });
    }

    [Theory]
    [InlineData("Entertainment", new[] { "YouTube", "Netflix", "Twitch", "Prime Video", "Disney+" })]
    [InlineData("Social", new[] { "Reddit", " / X", "Facebook", "Instagram", "LinkedIn" })]
    [InlineData("Productive", new[] { "GitHub", "Stack Overflow", "Google Docs", "Google Sheets" })]
    [InlineData("Communication", new[] { "Gmail", "Outlook", "WhatsApp" })]
    public void Seeds_title_rules_with_priority_10_and_no_exe_filter(string category, string[] patterns)
    {
        var titleRules = RulesFor(category).Where(r => r.MatchType == RuleMatchType.TitleContains).ToList();

        Assert.Equal(patterns, titleRules.Select(r => r.Pattern));
        Assert.All(titleRules, r =>
        {
            Assert.Null(r.ExeFilter);
            Assert.Equal(10, r.Priority);
        });
    }

    [Fact]
    public void Seeds_specific_exe_rules()
    {
        var rules = new RuleRepository(_connection);
        var categories = new CategoryRepository(_connection).GetAll().ToDictionary(c => c.Id, c => c.Name);

        Assert.Equal("Productive", categories[rules.GetExeRule("code.exe")!.CategoryId]);
        Assert.Equal("Communication", categories[rules.GetExeRule("olk.exe")!.CategoryId]);
        Assert.Equal("Browsing", categories[rules.GetExeRule("chrome.exe")!.CategoryId]);
        Assert.Equal("System", categories[rules.GetExeRule("startmenuexperiencehost.exe")!.CategoryId]);
    }

    [Fact]
    public void Seeded_rules_are_stamped_with_the_clock()
    {
        var rules = new RuleRepository(_connection).GetAll();

        Assert.Equal(38 + 17, rules.Count);
        Assert.All(rules, r => Assert.Equal(_clock.UtcNowMs, r.CreatedAt));
    }

    [Fact]
    public void Seeds_default_settings()
    {
        var repository = new SettingsRepository(_connection);

        Assert.Equal(AppSettings.Defaults, repository.Load());
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["poll_interval_ms"] = "1000",
                ["min_switch_seconds"] = "2",
                ["idle_threshold_seconds"] = "180",
                ["heartbeat_seconds"] = "30",
                ["day_start_hour"] = "0",
                ["web_port"] = "5217",
            },
            repository.GetAll());
    }

    private IEnumerable<Rule> RulesFor(string categoryName)
    {
        var category = new CategoryRepository(_connection).GetAll().Single(c => c.Name == categoryName);
        return new RuleRepository(_connection).GetAll().Where(r => r.CategoryId == category.Id);
    }
}
