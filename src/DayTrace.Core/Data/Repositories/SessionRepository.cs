using Dapper;
using DayTrace.Core.Models;
using Microsoft.Data.Sqlite;

namespace DayTrace.Core.Data.Repositories;

/// <summary>Access to <c>sessions</c>. Only the tracker writes this table.</summary>
public sealed class SessionRepository(SqliteConnection connection)
{
    private const string Columns = """
        id AS Id, started_at AS StartedAt, ended_at AS EndedAt, exe_name AS ExeName,
        exe_path AS ExePath, app_name AS AppName, window_title AS WindowTitle
        """;

    private const string InsertSql = """
        INSERT INTO sessions (started_at, ended_at, exe_name, exe_path, app_name, window_title)
        VALUES (@StartedAt, @EndedAt, @ExeName, @ExePath, @AppName, @WindowTitle)
        RETURNING id
        """;

    private const string UpdateEndSql = "UPDATE sessions SET ended_at = @EndedAt WHERE id = @Id";

    private const string DeleteSql = "DELETE FROM sessions WHERE id = @Id";

    private const string GetSql = $"SELECT {Columns} FROM sessions WHERE id = @Id";

    private const string OverlappingSql = $"""
        SELECT {Columns} FROM sessions
        WHERE started_at < @ToMs AND ended_at > @FromMs
        ORDER BY started_at, id
        """;

    private const string EarliestStartSql = "SELECT MIN(started_at) FROM sessions";

    private const string LatestEndSql = "SELECT MAX(ended_at) FROM sessions";

    /// <summary>Inserts the session (its <see cref="Session.Id"/> is ignored) and returns the new id.</summary>
    public long Insert(Session session) => connection.ExecuteScalar<long>(InsertSql, session);

    public void UpdateEnd(long id, long endedAt) =>
        connection.Execute(UpdateEndSql, new { Id = id, EndedAt = endedAt });

    public void Delete(long id) => connection.Execute(DeleteSql, new { Id = id });

    public Session? Get(long id) => connection.QuerySingleOrDefault<Session>(GetSql, new { Id = id });

    /// <summary>Sessions overlapping <c>[fromMs, toMs)</c>, unclipped, oldest first.</summary>
    public IReadOnlyList<Session> GetOverlapping(long fromMs, long toMs) =>
        connection.Query<Session>(OverlappingSql, new { FromMs = fromMs, ToMs = toMs }).AsList();

    public long? GetEarliestStart() => connection.ExecuteScalar<long?>(EarliestStartSql);

    public long? GetLatestEnd() => connection.ExecuteScalar<long?>(LatestEndSql);
}
