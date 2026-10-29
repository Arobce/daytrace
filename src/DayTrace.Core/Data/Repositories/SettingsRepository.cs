using Dapper;
using DayTrace.Core.Models;
using Microsoft.Data.Sqlite;

namespace DayTrace.Core.Data.Repositories;

/// <summary>Access to <c>settings</c>. Only the web process writes this table; the tracker reads it.</summary>
public sealed class SettingsRepository(SqliteConnection connection)
{
    private const string GetAllSql = "SELECT key AS Key, value AS Value FROM settings";

    private const string UpsertSql = """
        INSERT INTO settings (key, value) VALUES (@Key, @Value)
        ON CONFLICT (key) DO UPDATE SET value = excluded.value
        """;

    private sealed record Row(string Key, string Value);

    public IReadOnlyDictionary<string, string> GetAll() =>
        connection.Query<Row>(GetAllSql).ToDictionary(r => r.Key, r => r.Value);

    public AppSettings Load() => AppSettings.FromDictionary(GetAll());

    /// <summary>Upserts all values in one transaction.</summary>
    public void Set(IReadOnlyDictionary<string, string> values)
    {
        using var tx = connection.BeginTransaction();
        connection.Execute(UpsertSql, values.Select(kv => new Row(kv.Key, kv.Value)), tx);
        tx.Commit();
    }

    public void Save(AppSettings settings) => Set(settings.ToDictionary());
}
