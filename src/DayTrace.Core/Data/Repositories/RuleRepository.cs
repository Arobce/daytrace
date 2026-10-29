using Dapper;
using DayTrace.Core.Models;
using Microsoft.Data.Sqlite;

namespace DayTrace.Core.Data.Repositories;

/// <summary>Access to <c>rules</c>. Only the web process writes this table.</summary>
public sealed class RuleRepository(SqliteConnection connection)
{
    private const string Columns = """
        id AS Id, category_id AS CategoryId, match_type AS MatchType, pattern AS Pattern,
        exe_filter AS ExeFilter, priority AS Priority, created_at AS CreatedAt
        """;

    private const string GetAllSql = $"SELECT {Columns} FROM rules ORDER BY id";

    private const string GetSql = $"SELECT {Columns} FROM rules WHERE id = @Id";

    private const string GetExeRuleSql = $"""
        SELECT {Columns} FROM rules
        WHERE match_type = 'exe' AND pattern = @ExeName COLLATE NOCASE
        ORDER BY priority DESC, created_at DESC, id DESC
        LIMIT 1
        """;

    private const string InsertSql = """
        INSERT INTO rules (category_id, match_type, pattern, exe_filter, priority, created_at)
        VALUES (@CategoryId, @MatchType, @Pattern, @ExeFilter, @Priority, @CreatedAt)
        RETURNING id
        """;

    private const string UpdateSql = """
        UPDATE rules
        SET category_id = @CategoryId, match_type = @MatchType, pattern = @Pattern,
            exe_filter = @ExeFilter, priority = @Priority
        WHERE id = @Id
        """;

    private const string DeleteSql = "DELETE FROM rules WHERE id = @Id";

    private sealed record Row(
        long Id, long CategoryId, string MatchType, string Pattern, string? ExeFilter, long Priority, long CreatedAt)
    {
        public Rule ToModel() =>
            new(Id, CategoryId, DbText.ToMatchType(MatchType), Pattern, ExeFilter, checked((int)Priority), CreatedAt);
    }

    public IReadOnlyList<Rule> GetAll() => connection.Query<Row>(GetAllSql).Select(r => r.ToModel()).ToList();

    public Rule? Get(long id) => connection.QuerySingleOrDefault<Row>(GetSql, new { Id = id })?.ToModel();

    /// <summary>The exe rule for <paramref name="exeName"/> (case-insensitive), if any.</summary>
    public Rule? GetExeRule(string exeName) =>
        connection.QuerySingleOrDefault<Row>(GetExeRuleSql, new { ExeName = exeName })?.ToModel();

    /// <summary>Inserts the rule (its <see cref="Rule.Id"/> is ignored) and returns the new id.</summary>
    public long Insert(Rule rule) => connection.ExecuteScalar<long>(InsertSql, ToParams(rule));

    /// <summary>Updates everything except <see cref="Rule.CreatedAt"/>.</summary>
    /// <returns>False if no rule has that id.</returns>
    public bool Update(Rule rule) => connection.Execute(UpdateSql, ToParams(rule)) > 0;

    /// <returns>False if no rule has that id.</returns>
    public bool Delete(long id) => connection.Execute(DeleteSql, new { Id = id }) > 0;

    private static object ToParams(Rule rule) => new
    {
        rule.Id,
        rule.CategoryId,
        MatchType = DbText.From(rule.MatchType),
        rule.Pattern,
        rule.ExeFilter,
        rule.Priority,
        rule.CreatedAt,
    };
}
