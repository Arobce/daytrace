using DayTrace.Core.Data.Repositories;
using DayTrace.Core.Models;
using Microsoft.Data.Sqlite;

namespace DayTrace.Core.Tests;

public sealed class RepositoryTests : IDisposable
{
    private const long T0 = FakeClock.DefaultStart;

    private readonly TempDatabase _db = new();
    private readonly SqliteConnection _connection;

    public RepositoryTests()
    {
        _connection = _db.OpenMigrated();
    }

    public void Dispose()
    {
        _connection.Dispose();
        _db.Dispose();
    }

    [Fact]
    public void Session_round_trips_and_updates_end()
    {
        var sessions = new SessionRepository(_connection);
        var id = sessions.Insert(new Session(0, T0, T0, "code.exe", @"C:\VS Code\Code.exe", "Visual Studio Code", "PRD.md"));

        sessions.UpdateEnd(id, T0 + 30_000);

        Assert.Equal(
            new Session(id, T0, T0 + 30_000, "code.exe", @"C:\VS Code\Code.exe", "Visual Studio Code", "PRD.md"),
            sessions.Get(id));
    }

    [Fact]
    public void Session_with_null_path_and_app_name_round_trips()
    {
        var sessions = new SessionRepository(_connection);
        var id = sessions.Insert(new Session(0, T0, T0 + 1000, "unknown.exe", null, null, ""));

        Assert.Equal(new Session(id, T0, T0 + 1000, "unknown.exe", null, null, ""), sessions.Get(id));
    }

    [Fact]
    public void Session_delete_removes_row()
    {
        var sessions = new SessionRepository(_connection);
        var id = sessions.Insert(new Session(0, T0, T0, "code.exe", null, null, ""));

        sessions.Delete(id);

        Assert.Null(sessions.Get(id));
    }

    [Fact]
    public void GetOverlapping_includes_sessions_crossing_the_range_edges()
    {
        var sessions = new SessionRepository(_connection);
        var before = sessions.Insert(new Session(0, T0 - 2000, T0 - 1000, "a.exe", null, null, ""));
        var crossingStart = sessions.Insert(new Session(0, T0 - 1000, T0 + 1000, "b.exe", null, null, ""));
        var inside = sessions.Insert(new Session(0, T0 + 2000, T0 + 3000, "c.exe", null, null, ""));
        var crossingEnd = sessions.Insert(new Session(0, T0 + 9000, T0 + 11_000, "d.exe", null, null, ""));
        var after = sessions.Insert(new Session(0, T0 + 10_000, T0 + 12_000, "e.exe", null, null, ""));

        var ids = sessions.GetOverlapping(T0, T0 + 10_000).Select(s => s.Id);

        Assert.Equal([crossingStart, inside, crossingEnd], ids);
        Assert.DoesNotContain(before, ids);
        Assert.DoesNotContain(after, ids);
    }

    [Fact]
    public void Earliest_start_and_latest_end_are_null_when_empty()
    {
        var sessions = new SessionRepository(_connection);
        Assert.Null(sessions.GetEarliestStart());
        Assert.Null(sessions.GetLatestEnd());

        sessions.Insert(new Session(0, T0, T0 + 5000, "a.exe", null, null, ""));
        sessions.Insert(new Session(0, T0 - 9000, T0 - 8000, "b.exe", null, null, ""));

        Assert.Equal(T0 - 9000, sessions.GetEarliestStart());
        Assert.Equal(T0 + 5000, sessions.GetLatestEnd());
    }

    [Theory]
    [InlineData(AwayReason.Idle)]
    [InlineData(AwayReason.Locked)]
    [InlineData(AwayReason.Asleep)]
    [InlineData(AwayReason.Paused)]
    public void Away_period_round_trips_every_reason(AwayReason reason)
    {
        var away = new AwayPeriodRepository(_connection);
        var id = away.Insert(new AwayPeriod(0, T0, T0, reason));

        away.UpdateEnd(id, T0 + 60_000);

        Assert.Equal(new AwayPeriod(id, T0, T0 + 60_000, reason), away.Get(id));
        Assert.Equal(T0 + 60_000, away.GetLatestEnd());
        Assert.Single(away.GetOverlapping(T0, T0 + 1));
    }

    [Fact]
    public void Deleting_a_category_cascades_to_its_rules()
    {
        var categories = new CategoryRepository(_connection);
        var rules = new RuleRepository(_connection);
        var categoryId = categories.Insert(new Category(0, "Games", "#123456", Productivity.Distracting, 9, false));
        var ruleId = rules.Insert(new Rule(0, categoryId, RuleMatchType.Exe, "game.exe", null, 0, T0));

        Assert.True(categories.Delete(categoryId));

        Assert.Null(rules.Get(ruleId));
        Assert.False(categories.Delete(categoryId));
    }

    [Fact]
    public void Category_update_never_changes_is_system()
    {
        var categories = new CategoryRepository(_connection);
        var uncategorized = categories.GetUncategorized();

        categories.Update(uncategorized with { Color = "#000000", IsSystem = false });

        Assert.Equal(uncategorized with { Color = "#000000" }, categories.Get(uncategorized.Id));
    }

    [Fact]
    public void Rule_insert_rejects_unknown_category()
    {
        var rules = new RuleRepository(_connection);

        Assert.Throws<SqliteException>(() => rules.Insert(new Rule(0, 9999, RuleMatchType.Exe, "x.exe", null, 0, T0)));
    }

    [Fact]
    public void Title_rule_round_trips_with_exe_filter_and_update()
    {
        var rules = new RuleRepository(_connection);
        var categoryId = new CategoryRepository(_connection).GetUncategorized().Id;
        var id = rules.Insert(new Rule(0, categoryId, RuleMatchType.TitleContains, "Jira", "chrome.exe", 5, T0));

        Assert.True(rules.Update(new Rule(id, categoryId, RuleMatchType.TitleContains, "Jira", "msedge.exe", 7, 0)));

        Assert.Equal(new Rule(id, categoryId, RuleMatchType.TitleContains, "Jira", "msedge.exe", 7, T0), rules.Get(id));
    }

    [Fact]
    public void GetExeRule_is_case_insensitive_and_ignores_title_rules()
    {
        var rules = new RuleRepository(_connection);

        Assert.Equal("chrome.exe", rules.GetExeRule("Chrome.EXE")?.Pattern);
        Assert.Null(rules.GetExeRule("YouTube"));
        Assert.Null(rules.GetExeRule("nothing.exe"));
    }

    [Fact]
    public void Settings_upsert_updates_existing_and_adds_new_keys()
    {
        var settings = new SettingsRepository(_connection);

        settings.Save(AppSettings.Defaults with { IdleThresholdSeconds = 300, DayStartHour = 4 });

        Assert.Equal(AppSettings.Defaults with { IdleThresholdSeconds = 300, DayStartHour = 4 }, settings.Load());
        Assert.Equal(6, settings.GetAll().Count);
    }

    [Fact]
    public void Settings_fall_back_to_defaults_for_bad_values()
    {
        var parsed = AppSettings.FromDictionary(new Dictionary<string, string>
        {
            [SettingKeys.PollIntervalMs] = "fast",
            [SettingKeys.WebPort] = "6000",
        });

        Assert.Equal(AppSettings.Defaults with { WebPort = 6000 }, parsed);
    }
}
