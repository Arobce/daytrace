using Microsoft.Data.Sqlite;

namespace DayTrace.Core.Data;

public static class Db
{
    /// <summary><c>%LOCALAPPDATA%\DayTrace</c>.</summary>
    public static string DefaultDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DayTrace");

    public static string DefaultDbPath { get; } = Path.Combine(DefaultDataDirectory, "daytrace.db");

    /// <summary>
    /// Opens (creating if needed) the SQLite file with the settings both processes rely on:
    /// WAL so the tracker and web can share the file, a busy timeout, and enforced foreign keys.
    /// Does not migrate; call <see cref="Migrator.Migrate"/> after opening.
    /// </summary>
    public static SqliteConnection Open(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        try
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA journal_mode = WAL;
                PRAGMA busy_timeout = 5000;
                PRAGMA foreign_keys = ON;
                """;
            command.ExecuteNonQuery();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }
}
