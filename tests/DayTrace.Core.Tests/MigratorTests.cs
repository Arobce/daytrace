using Dapper;
using DayTrace.Core.Data;

namespace DayTrace.Core.Tests;

public sealed class MigratorTests : IDisposable
{
    private readonly TempDatabase _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Open_sets_wal_busy_timeout_and_foreign_keys()
    {
        using var connection = _db.Open();

        Assert.Equal("wal", connection.ExecuteScalar<string>("PRAGMA journal_mode"));
        Assert.Equal(5000, connection.ExecuteScalar<int>("PRAGMA busy_timeout"));
        Assert.Equal(1, connection.ExecuteScalar<int>("PRAGMA foreign_keys"));
    }

    [Fact]
    public void Fresh_database_is_migrated_to_latest_version()
    {
        using var connection = _db.Open();
        Assert.Equal(0, Migrator.GetVersion(connection));

        var applied = Migrator.Migrate(connection, new FakeClock());

        Assert.Equal(Migrator.LatestVersion, applied);
        Assert.Equal(Migrator.LatestVersion, Migrator.GetVersion(connection));
    }

    [Fact]
    public void Fresh_database_has_all_tables_and_indexes()
    {
        using var connection = _db.OpenMigrated();

        var names = connection
            .Query<string>("SELECT name FROM sqlite_master WHERE type IN ('table','index') AND name NOT LIKE 'sqlite_%'")
            .ToHashSet();

        string[] expected =
        [
            "sessions", "away_periods", "categories", "rules", "settings",
            "ix_sessions_started", "ix_sessions_exe", "ix_away_started",
        ];
        Assert.Subset(names, expected.ToHashSet());
    }

    [Fact]
    public void Running_twice_is_a_no_op()
    {
        using (var first = _db.OpenMigrated())
        {
        }

        using var connection = _db.Open();
        var countsBefore = RowCounts(connection);

        var applied = Migrator.Migrate(connection, new FakeClock());

        Assert.Equal(0, applied);
        Assert.Equal(Migrator.LatestVersion, Migrator.GetVersion(connection));
        Assert.Equal(countsBefore, RowCounts(connection));
    }

    [Fact]
    public void Concurrent_migrations_apply_exactly_once()
    {
        // Tracker and Web may both open a fresh DB at startup.
        var results = new int[2];
        Parallel.For(0, 2, i =>
        {
            using var connection = _db.Open();
            results[i] = Migrator.Migrate(connection, new FakeClock());
        });

        Assert.Equal(Migrator.LatestVersion, results.Sum());
        using var check = _db.Open();
        Assert.Equal(7, check.ExecuteScalar<int>("SELECT COUNT(*) FROM categories"));
    }

    [Fact]
    public void Database_newer_than_this_build_is_rejected()
    {
        using var connection = _db.Open();
        connection.Execute($"PRAGMA user_version = {Migrator.LatestVersion + 1}");

        Assert.Throws<InvalidOperationException>(() => Migrator.Migrate(connection, new FakeClock()));
    }

    private static (int Categories, int Rules, int Settings) RowCounts(Microsoft.Data.Sqlite.SqliteConnection c) => (
        c.ExecuteScalar<int>("SELECT COUNT(*) FROM categories"),
        c.ExecuteScalar<int>("SELECT COUNT(*) FROM rules"),
        c.ExecuteScalar<int>("SELECT COUNT(*) FROM settings"));
}
