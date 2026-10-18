using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Data.Sqlite;
using ClipNest.Core.Abstractions;
using ClipNest.Core.Models;

namespace ClipNest.Core.Persistence;

/// <summary>SQLite-backed <see cref="IClipboardStore"/>.</summary>
public sealed class SqliteClipboardStore : IClipboardStore
{
    private const string SelectColumns =
        "id, created_at, kind, content, blob_path, content_hash, source_app, is_favorite";

    private readonly ClipNestDatabase _database;
    private long _revision;
    /// <summary>Changes whenever this shared store commits a history mutation.</summary>
    public long Revision => System.Threading.Interlocked.Read(ref _revision);
    private void Changed() => System.Threading.Interlocked.Increment(ref _revision);

    public SqliteClipboardStore(ClipNestDatabase database)
        => _database = database ?? throw new ArgumentNullException(nameof(database));

    public AddResult Add(ClipboardEntry entry, int historyLimit)
    {
        ArgumentNullException.ThrowIfNull(entry);

        using var conn = _database.Open();
        using var tx = conn.BeginTransaction();
        // A capture may have read the previous setting before a concurrent settings save.
        using (var limit = conn.CreateCommand())
        {
            limit.Transaction = tx;
            limit.CommandText = "SELECT value FROM app_settings WHERE key = 'history_limit';";
            if (int.TryParse(limit.ExecuteScalar() as string, out var currentLimit) && currentLimit > 0)
                historyLimit = currentLimit;
        }

        var existingId = FindIdByHash(conn, tx, entry.ContentHash, entry.Kind);
        if (existingId is not null)
        {
            BumpTimestamp(conn, tx, existingId, entry.CreatedAt);
            tx.Commit();
            Changed();

            var dedupOrphans = entry.BlobPath is null
                ? Array.Empty<string>()
                : new[] { entry.BlobPath };
            return new AddResult(WasDeduplicated: true, dedupOrphans);
        }

        Insert(conn, tx, entry);
        var trimmedBlobs = TrimToHistoryLimit(conn, tx, historyLimit);
        tx.Commit();
        Changed();

        return new AddResult(WasDeduplicated: false, trimmedBlobs);
    }

    public ClipboardEntry? GetMostRecent()
    {
        using var conn = _database.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"SELECT {SelectColumns} FROM clipboard_entries ORDER BY created_at DESC LIMIT 1;";
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadEntry(reader) : null;
    }

    public IReadOnlyList<ClipboardEntry> GetRecent(int count)
    {
        using var conn = _database.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"SELECT {SelectColumns} FROM clipboard_entries ORDER BY created_at DESC LIMIT $n;";
        cmd.Parameters.AddWithValue("$n", count);

        var result = new List<ClipboardEntry>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(ReadEntry(reader));
        }

        return result;
    }

    public IReadOnlyList<ImageBlobReference> GetImageBlobReferences()
    {
        using var conn = _database.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT id, blob_path FROM clipboard_entries WHERE kind = 'image' AND blob_path IS NOT NULL;";

        var result = new List<ImageBlobReference>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new ImageBlobReference(Guid.Parse(reader.GetString(0)), reader.GetString(1)));
        }

        return result;
    }

    public IReadOnlyList<ClipboardEntry> Search(string query, bool favoritesOnly, string? kind = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        using var conn = _database.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT {SelectColumns} FROM clipboard_entries
            WHERE ($favorites = 0 OR is_favorite = 1)
              AND ($kind IS NULL OR kind = $kind)
              AND ($query = '' OR instr(lower(coalesce(content, '')), lower($query)) > 0
                   OR instr(lower(coalesce(source_app, '')), lower($query)) > 0)
            ORDER BY created_at DESC LIMIT 500;
            """;
        cmd.Parameters.AddWithValue("$favorites", favoritesOnly ? 1 : 0);
        cmd.Parameters.AddWithValue("$kind", (object?)kind ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$query", query.Trim());
        using var reader = cmd.ExecuteReader();
        var result = new List<ClipboardEntry>();
        while (reader.Read()) result.Add(ReadEntry(reader));
        return result;
    }

    /// <summary>Reads bounded previews instead of materializing every matching full payload.</summary>
    public ClipboardHistoryPage GetPage(string query, bool favoritesOnly, string? kind = null,
        int offset = 0, int count = 100, System.Threading.CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        cancellationToken.ThrowIfCancellationRequested();
        var revision = Revision;
        using var conn = _database.Open();
        using var tx = conn.BeginTransaction(deferred: true);
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        const string where = """
            FROM clipboard_entries WHERE ($favorites = 0 OR is_favorite = 1)
              AND ($kind IS NULL OR kind = $kind)
              AND ($query = '' OR instr(lower(coalesce(content, '')), lower($query)) > 0
                   OR instr(lower(coalesce(source_app, '')), lower($query)) > 0)
            """;
        cmd.CommandText = "SELECT COUNT(*) " + where;
        cmd.Parameters.AddWithValue("$favorites", favoritesOnly ? 1 : 0);
        cmd.Parameters.AddWithValue("$kind", (object?)kind ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$query", query.Trim());
        var total = Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        cancellationToken.ThrowIfCancellationRequested();
        if (total == 0 || offset >= total)
        {
            tx.Commit();
            return new ClipboardHistoryPage(Array.Empty<ClipboardEntrySummary>(), total, revision);
        }
        cmd.CommandText = """
            SELECT id, created_at, kind,
                CASE WHEN kind IN ('text', 'file', 'color') THEN substr(content, 1, 161) ELSE NULL END,
                blob_path, source_app, is_favorite
            """ + " " + where + " ORDER BY created_at DESC, id DESC LIMIT $count OFFSET $offset;";
        cmd.Parameters.AddWithValue("$count", count);
        cmd.Parameters.AddWithValue("$offset", offset);
        var result = new List<ClipboardEntrySummary>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                result.Add(new ClipboardEntrySummary(Guid.Parse(reader.GetString(0)),
                    DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1)),
                    ClipboardKindExtensions.ParseClipboardKind(reader.GetString(2)),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetBoolean(6)));
            }
        }
        tx.Commit();
        return new ClipboardHistoryPage(result, total, revision);
    }

    /// <summary>Loads the complete payload only when selected or copied.</summary>
    public ClipboardEntry? GetById(Guid id)
    {
        using var conn = _database.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM clipboard_entries WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadEntry(reader) : null;
    }

    /// <summary>Atomically saves the limit and trims existing non-favorites. Caller removes returned blobs.</summary>
    public IReadOnlyList<string> SetHistoryLimitAndTrim(int limit)
    {
        if (limit is < 10 or > 10000) throw new ArgumentOutOfRangeException(nameof(limit));
        using var conn = _database.Open();
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO app_settings (key, value) VALUES ('history_limit', $value) ON CONFLICT(key) DO UPDATE SET value = excluded.value;";
        cmd.Parameters.AddWithValue("$value", limit.ToString(CultureInfo.InvariantCulture));
        cmd.ExecuteNonQuery();
        var orphans = TrimToHistoryLimit(conn, tx, limit);
        tx.Commit();
        Changed();
        return orphans;
    }

    public void SetFavorite(Guid id, bool favorite)
    {
        using var conn = _database.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE clipboard_entries SET is_favorite = $favorite WHERE id = $id;";
        cmd.Parameters.AddWithValue("$favorite", favorite ? 1 : 0);
        cmd.Parameters.AddWithValue("$id", id.ToString());
        cmd.ExecuteNonQuery();
        Changed();
    }

    public IReadOnlyList<string> ClearHistory()
    {
        using var conn = _database.Open();
        using var tx = conn.BeginTransaction();
        var blobs = new List<string>();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT blob_path FROM clipboard_entries WHERE is_favorite = 0 AND blob_path IS NOT NULL;";
        using (var reader = cmd.ExecuteReader())
            while (reader.Read()) blobs.Add(reader.GetString(0));
        cmd.CommandText = "DELETE FROM clipboard_entries WHERE is_favorite = 0;";
        cmd.ExecuteNonQuery();
        tx.Commit();
        Changed();
        return blobs;
    }

    public void DeleteById(Guid id)
    {
        using var conn = _database.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM clipboard_entries WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        cmd.ExecuteNonQuery();
        Changed();
    }

    private static string? FindIdByHash(SqliteConnection conn, SqliteTransaction tx, string hash, ClipboardKind kind)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT id FROM clipboard_entries WHERE content_hash = $h AND kind = $kind LIMIT 1;";
        cmd.Parameters.AddWithValue("$h", hash);
        cmd.Parameters.AddWithValue("$kind", kind.ToDbString());
        return cmd.ExecuteScalar() as string;
    }

    private static void BumpTimestamp(
        SqliteConnection conn, SqliteTransaction tx, string id, DateTimeOffset createdAt)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE clipboard_entries SET created_at = $t WHERE id = $id;";
        cmd.Parameters.AddWithValue("$t", createdAt.ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    private static void Insert(SqliteConnection conn, SqliteTransaction tx, ClipboardEntry entry)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO clipboard_entries
                (id, created_at, kind, content, blob_path, content_hash, source_app, is_favorite)
            VALUES ($id, $ca, $k, $c, $bp, $h, $sa, $favorite);
            """;
        cmd.Parameters.AddWithValue("$id", entry.Id.ToString());
        cmd.Parameters.AddWithValue("$ca", entry.CreatedAt.ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("$k", entry.Kind.ToDbString());
        cmd.Parameters.AddWithValue("$c", (object?)entry.Content ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$bp", (object?)entry.BlobPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$h", entry.ContentHash);
        cmd.Parameters.AddWithValue("$sa", (object?)entry.SourceApp ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$favorite", entry.IsFavorite ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Deletes rows beyond <paramref name="historyLimit"/>; returns trimmed image blob paths.</summary>
    private static IReadOnlyList<string> TrimToHistoryLimit(
        SqliteConnection conn, SqliteTransaction tx, int historyLimit)
    {
        long total;
        using (var count = conn.CreateCommand())
        {
            count.Transaction = tx;
            count.CommandText = "SELECT COUNT(*) FROM clipboard_entries WHERE is_favorite = 0;";
            total = Convert.ToInt64(count.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        if (total <= historyLimit)
        {
            return Array.Empty<string>();
        }

        var excess = total - historyLimit;
        var trimmedBlobs = new List<string>();

        using (var select = conn.CreateCommand())
        {
            select.Transaction = tx;
            select.CommandText =
                "SELECT id, blob_path FROM clipboard_entries WHERE is_favorite = 0 ORDER BY created_at ASC, id ASC LIMIT $n;";
            select.Parameters.AddWithValue("$n", excess);
            using var reader = select.ExecuteReader();
            while (reader.Read())
            {
                if (!reader.IsDBNull(1))
                {
                    trimmedBlobs.Add(reader.GetString(1));
                }
            }
        }

        using var delete = conn.CreateCommand();
        delete.Transaction = tx;
        delete.CommandText = "DELETE FROM clipboard_entries WHERE id IN (SELECT id FROM clipboard_entries WHERE is_favorite = 0 ORDER BY created_at ASC, id ASC LIMIT $n);";
        delete.Parameters.AddWithValue("$n", excess);
        delete.ExecuteNonQuery();

        return trimmedBlobs;
    }

    private static ClipboardEntry ReadEntry(SqliteDataReader reader) => new(
        Id: Guid.Parse(reader.GetString(0)),
        CreatedAt: DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1)),
        Kind: ClipboardKindExtensions.ParseClipboardKind(reader.GetString(2)),
        Content: reader.IsDBNull(3) ? null : reader.GetString(3),
        BlobPath: reader.IsDBNull(4) ? null : reader.GetString(4),
        ContentHash: reader.GetString(5),
        SourceApp: reader.IsDBNull(6) ? null : reader.GetString(6),
        IsFavorite: reader.GetBoolean(7));
}
