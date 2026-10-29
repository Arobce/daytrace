using Microsoft.Data.Sqlite;

namespace DayTrace.Core.Data.Migrations;

/// <summary>One schema step. Applied inside the migrator's transaction; must use <paramref name="tx"/> for every command.</summary>
public interface IMigration
{
    /// <summary>The <c>PRAGMA user_version</c> the database has after this migration.</summary>
    int Version { get; }

    void Apply(SqliteConnection connection, SqliteTransaction tx, IClock clock);
}
