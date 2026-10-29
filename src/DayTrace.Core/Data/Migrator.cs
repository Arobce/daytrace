using System.Globalization;
using Dapper;
using DayTrace.Core.Data.Migrations;
using Microsoft.Data.Sqlite;

namespace DayTrace.Core.Data;

/// <summary>Brings a database up to <see cref="LatestVersion"/>, tracked with <c>PRAGMA user_version</c>.</summary>
public static class Migrator
{
    private static readonly IMigration[] All =
    [
        new M001_InitialSchema(),
        new M002_SeedData(),
    ];

    public static int LatestVersion => All[^1].Version;

    public static int GetVersion(SqliteConnection connection, SqliteTransaction? tx = null) =>
        connection.ExecuteScalar<int>("PRAGMA user_version", transaction: tx);

    /// <summary>
    /// Applies pending migrations in one transaction. Safe to call from both processes at once:
    /// the version is re-read after taking the write lock, so the loser of the race finds nothing to do.
    /// </summary>
    /// <returns>The number of migrations applied.</returns>
    public static int Migrate(SqliteConnection connection, IClock clock)
    {
        if (GetVersion(connection) == LatestVersion)
        {
            return 0;
        }

        // Microsoft.Data.Sqlite starts non-deferred transactions with BEGIN IMMEDIATE, taking the write lock up front.
        using var tx = connection.BeginTransaction(deferred: false);
        var current = GetVersion(connection, tx);
        if (current > LatestVersion)
        {
            throw new InvalidOperationException(
                $"Database schema version {current} is newer than this build supports ({LatestVersion}).");
        }

        var applied = 0;
        foreach (var migration in All.Where(m => m.Version > current))
        {
            migration.Apply(connection, tx, clock);
            // PRAGMA arguments can't be parameterized; Version is a compile-time int.
            connection.Execute(
                $"PRAGMA user_version = {migration.Version.ToString(CultureInfo.InvariantCulture)}",
                transaction: tx);
            applied++;
        }

        tx.Commit();
        return applied;
    }
}
