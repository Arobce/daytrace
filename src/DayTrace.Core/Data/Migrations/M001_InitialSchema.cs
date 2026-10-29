using Dapper;
using Microsoft.Data.Sqlite;

namespace DayTrace.Core.Data.Migrations;

/// <summary>Tables from PRD §5.</summary>
internal sealed class M001_InitialSchema : IMigration
{
    private const string Sql = """
        CREATE TABLE sessions (
          id              INTEGER PRIMARY KEY,
          started_at      INTEGER NOT NULL,
          ended_at        INTEGER NOT NULL,
          exe_name        TEXT    NOT NULL,
          exe_path        TEXT,
          app_name        TEXT,
          window_title    TEXT    NOT NULL DEFAULT ''
        );
        CREATE INDEX ix_sessions_started ON sessions(started_at);
        CREATE INDEX ix_sessions_exe     ON sessions(exe_name);

        CREATE TABLE away_periods (
          id          INTEGER PRIMARY KEY,
          started_at  INTEGER NOT NULL,
          ended_at    INTEGER NOT NULL,
          reason      TEXT NOT NULL CHECK (reason IN ('idle','locked','asleep','paused'))
        );
        CREATE INDEX ix_away_started ON away_periods(started_at);

        CREATE TABLE categories (
          id            INTEGER PRIMARY KEY,
          name          TEXT NOT NULL UNIQUE,
          color         TEXT NOT NULL,
          productivity  TEXT NOT NULL CHECK (productivity IN ('productive','neutral','distracting')),
          sort_order    INTEGER NOT NULL DEFAULT 0,
          is_system     INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE rules (
          id           INTEGER PRIMARY KEY,
          category_id  INTEGER NOT NULL REFERENCES categories(id) ON DELETE CASCADE,
          match_type   TEXT NOT NULL CHECK (match_type IN ('exe','title_contains')),
          pattern      TEXT NOT NULL,
          exe_filter   TEXT,
          priority     INTEGER NOT NULL DEFAULT 0,
          created_at   INTEGER NOT NULL,
          UNIQUE (match_type, pattern, exe_filter)
        );

        CREATE TABLE settings (
          key    TEXT PRIMARY KEY,
          value  TEXT NOT NULL
        );
        """;

    public int Version => 1;

    public void Apply(SqliteConnection connection, SqliteTransaction tx, IClock clock) =>
        connection.Execute(Sql, transaction: tx);
}
