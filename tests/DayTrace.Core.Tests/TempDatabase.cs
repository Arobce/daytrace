using DayTrace.Core.Data;
using Microsoft.Data.Sqlite;

namespace DayTrace.Core.Tests;

/// <summary>A SQLite file in a fresh temp folder, deleted on dispose.</summary>
public sealed class TempDatabase : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "DayTrace.Tests", Guid.NewGuid().ToString("N"));

    public TempDatabase()
    {
        FilePath = Path.Combine(_directory, "daytrace.db");
    }

    public string FilePath { get; }

    public SqliteConnection Open() => Db.Open(FilePath);

    /// <summary>Opens the database and migrates it to the latest version.</summary>
    public SqliteConnection OpenMigrated(IClock? clock = null)
    {
        var connection = Open();
        Migrator.Migrate(connection, clock ?? new FakeClock());
        return connection;
    }

    public void Dispose()
    {
        // Pooled connections keep the file open; release them before deleting.
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort; a leftover temp folder must not fail a test.
        }
    }
}
