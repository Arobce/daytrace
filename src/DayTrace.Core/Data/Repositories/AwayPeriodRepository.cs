using Dapper;
using DayTrace.Core.Models;
using Microsoft.Data.Sqlite;

namespace DayTrace.Core.Data.Repositories;

/// <summary>Access to <c>away_periods</c>. Only the tracker writes this table.</summary>
public sealed class AwayPeriodRepository(SqliteConnection connection)
{
    private const string Columns = "id AS Id, started_at AS StartedAt, ended_at AS EndedAt, reason AS Reason";

    private const string InsertSql = """
        INSERT INTO away_periods (started_at, ended_at, reason)
        VALUES (@StartedAt, @EndedAt, @Reason)
        RETURNING id
        """;

    private const string UpdateEndSql = "UPDATE away_periods SET ended_at = @EndedAt WHERE id = @Id";

    private const string DeleteSql = "DELETE FROM away_periods WHERE id = @Id";

    private const string GetSql = $"SELECT {Columns} FROM away_periods WHERE id = @Id";

    private const string OverlappingSql = $"""
        SELECT {Columns} FROM away_periods
        WHERE started_at < @ToMs AND ended_at > @FromMs
        ORDER BY started_at, id
        """;

    private const string LatestEndSql = "SELECT MAX(ended_at) FROM away_periods";

    private sealed record Row(long Id, long StartedAt, long EndedAt, string Reason)
    {
        public AwayPeriod ToModel() => new(Id, StartedAt, EndedAt, DbText.ToAwayReason(Reason));
    }

    /// <summary>Inserts the period (its <see cref="AwayPeriod.Id"/> is ignored) and returns the new id.</summary>
    public long Insert(AwayPeriod period) => connection.ExecuteScalar<long>(InsertSql, new
    {
        period.StartedAt,
        period.EndedAt,
        Reason = DbText.From(period.Reason),
    });

    public void UpdateEnd(long id, long endedAt) =>
        connection.Execute(UpdateEndSql, new { Id = id, EndedAt = endedAt });

    public void Delete(long id) => connection.Execute(DeleteSql, new { Id = id });

    public AwayPeriod? Get(long id) => connection.QuerySingleOrDefault<Row>(GetSql, new { Id = id })?.ToModel();

    /// <summary>Away periods overlapping <c>[fromMs, toMs)</c>, unclipped, oldest first.</summary>
    public IReadOnlyList<AwayPeriod> GetOverlapping(long fromMs, long toMs) =>
        connection.Query<Row>(OverlappingSql, new { FromMs = fromMs, ToMs = toMs }).Select(r => r.ToModel()).ToList();

    public long? GetLatestEnd() => connection.ExecuteScalar<long?>(LatestEndSql);
}
