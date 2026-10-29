using Dapper;
using DayTrace.Core.Models;
using Microsoft.Data.Sqlite;

namespace DayTrace.Core.Data.Repositories;

/// <summary>Access to <c>categories</c>. Only the web process writes this table.</summary>
public sealed class CategoryRepository(SqliteConnection connection)
{
    private const string Columns = """
        id AS Id, name AS Name, color AS Color, productivity AS Productivity,
        sort_order AS SortOrder, is_system AS IsSystem
        """;

    private const string GetAllSql = $"SELECT {Columns} FROM categories ORDER BY sort_order, id";

    private const string GetSql = $"SELECT {Columns} FROM categories WHERE id = @Id";

    private const string GetSystemSql = $"SELECT {Columns} FROM categories WHERE is_system = 1 ORDER BY id LIMIT 1";

    private const string InsertSql = """
        INSERT INTO categories (name, color, productivity, sort_order, is_system)
        VALUES (@Name, @Color, @Productivity, @SortOrder, @IsSystem)
        RETURNING id
        """;

    private const string UpdateSql = """
        UPDATE categories
        SET name = @Name, color = @Color, productivity = @Productivity, sort_order = @SortOrder
        WHERE id = @Id
        """;

    private const string DeleteSql = "DELETE FROM categories WHERE id = @Id";

    private sealed record Row(long Id, string Name, string Color, string Productivity, long SortOrder, long IsSystem)
    {
        public Category ToModel() =>
            new(Id, Name, Color, DbText.ToProductivity(Productivity), checked((int)SortOrder), IsSystem != 0);
    }

    public IReadOnlyList<Category> GetAll() =>
        connection.Query<Row>(GetAllSql).Select(r => r.ToModel()).ToList();

    public Category? Get(long id) => connection.QuerySingleOrDefault<Row>(GetSql, new { Id = id })?.ToModel();

    /// <summary>The 'Uncategorized' category (the one with <c>is_system = 1</c>).</summary>
    public Category GetUncategorized() =>
        connection.QuerySingleOrDefault<Row>(GetSystemSql)?.ToModel()
        ?? throw new InvalidOperationException("The system 'Uncategorized' category is missing.");

    /// <summary>Inserts the category (its <see cref="Category.Id"/> is ignored) and returns the new id.</summary>
    public long Insert(Category category) => connection.ExecuteScalar<long>(InsertSql, new
    {
        category.Name,
        category.Color,
        Productivity = DbText.From(category.Productivity),
        category.SortOrder,
        IsSystem = category.IsSystem ? 1 : 0,
    });

    /// <summary>Updates name, color, productivity and sort order. <see cref="Category.IsSystem"/> is never changed.</summary>
    /// <returns>False if no category has that id.</returns>
    public bool Update(Category category) => connection.Execute(UpdateSql, new
    {
        category.Id,
        category.Name,
        category.Color,
        Productivity = DbText.From(category.Productivity),
        category.SortOrder,
    }) > 0;

    /// <summary>Deletes the category; its rules go with it (ON DELETE CASCADE).</summary>
    /// <returns>False if no category has that id.</returns>
    public bool Delete(long id) => connection.Execute(DeleteSql, new { Id = id }) > 0;
}
