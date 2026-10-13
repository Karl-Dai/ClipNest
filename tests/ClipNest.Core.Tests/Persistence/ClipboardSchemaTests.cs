using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using ClipNest.Core.Persistence;
using Xunit;

namespace ClipNest.Core.Tests.Persistence;

public class ClipboardSchemaTests : IDisposable
{
    private readonly string _dbFile;

    public ClipboardSchemaTests()
        => _dbFile = Path.Combine(Path.GetTempPath(), "clipnest-schema-" + Path.GetRandomFileName() + ".db");

    [Fact]
    public void Migration_FromV1PreservesExistingHistory()
    {
        using var conn = OpenRaw();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE clipboard_entries (id TEXT PRIMARY KEY, created_at INTEGER, kind TEXT,
                content TEXT, blob_path TEXT, content_hash TEXT, source_app TEXT);
            INSERT INTO clipboard_entries VALUES ('legacy', 1, 'text', 'keep me', NULL, 'hash', 'test');
            PRAGMA user_version = 1;
            """;
        cmd.ExecuteNonQuery();
        ClipboardSchema.EnsureCreated(conn);
        ScalarText(conn, "SELECT content FROM clipboard_entries WHERE id='legacy';").Should().Be("keep me");
        cmd.CommandText = "SELECT is_favorite FROM clipboard_entries WHERE id='legacy';";
        Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture).Should().Be(0);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbFile))
        {
            File.Delete(_dbFile);
        }

        GC.SuppressFinalize(this);
    }

    private SqliteConnection OpenRaw()
    {
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _dbFile }.ToString());
        conn.Open();
        return conn;
    }

    [Fact]
    public void EnsureCreated_CreatesTablesIndexesAndStampsUserVersion()
    {
        using (var conn = OpenRaw())
        {
            ClipboardSchema.EnsureCreated(conn);
        }

        using var check = OpenRaw();

        ScalarText(check, "SELECT name FROM sqlite_master WHERE type='table' AND name='clipboard_entries';")
            .Should().Be("clipboard_entries");
        ScalarText(check, "SELECT name FROM sqlite_master WHERE type='table' AND name='app_settings';")
            .Should().Be("app_settings");

        var indexes = QueryTextColumn(check,
            "SELECT name FROM sqlite_master WHERE type='index' AND tbl_name='clipboard_entries';");
        indexes.Should().Contain("ix_entries_created_at");
        indexes.Should().Contain("ix_entries_hash");

        using var pragma = check.CreateCommand();
        pragma.CommandText = "PRAGMA user_version;";
        Convert.ToInt32(pragma.ExecuteScalar(), CultureInfo.InvariantCulture)
            .Should().Be(ClipboardSchema.CurrentVersion);
    }

    [Fact]
    public void EnsureCreated_IsIdempotent()
    {
        using (var conn = OpenRaw())
        {
            ClipboardSchema.EnsureCreated(conn);
        }

        using var conn2 = OpenRaw();
        Action act = () => ClipboardSchema.EnsureCreated(conn2);
        act.Should().NotThrow();

        // The schema must still be intact after the second (idempotent) call.
        ScalarText(conn2, "SELECT name FROM sqlite_master WHERE type='table' AND name='clipboard_entries';")
            .Should().Be("clipboard_entries");
        ScalarText(conn2, "SELECT name FROM sqlite_master WHERE type='table' AND name='app_settings';")
            .Should().Be("app_settings");
    }

    private static string? ScalarText(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar() as string;
    }

    private static List<string> QueryTextColumn(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var result = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }
}
