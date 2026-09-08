using Microsoft.Data.Sqlite;
using Seiri.Core;
using Seiri.Core.Contracts;
using Seiri.Core.Models;

namespace Seiri.Infrastructure;

public sealed class SqliteLibraryIndex : ILibraryIndex
{
    private readonly SqliteConnection _connection;
    private readonly string _generatedFolderName;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public SqliteLibraryIndex(string rootPath, string generatedFolderName)
    {
        RootPath = LibraryNesting.Normalize(rootPath);
        _generatedFolderName = generatedFolderName;
        var dbPath = GeneratedLayout.GetDatabasePath(RootPath, generatedFolderName);
        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString());
    }

    public string RootPath { get; }
    public string GeneratedFolderName => _generatedFolderName;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
        await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA foreign_keys = ON;
            CREATE TABLE IF NOT EXISTS library_meta (
              key   TEXT PRIMARY KEY,
              value TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS media (
              id            INTEGER PRIMARY KEY,
              rel_path      TEXT NOT NULL UNIQUE,
              file_name     TEXT NOT NULL,
              ext           TEXT NOT NULL,
              kind          TEXT NOT NULL,
              byte_size     INTEGER NOT NULL,
              width         INTEGER,
              height        INTEGER,
              duration_ms   INTEGER,
              content_hash  TEXT,
              mtime_utc     TEXT NOT NULL,
              taken_at      TEXT,
              added_at      TEXT NOT NULL,
              is_favorite   INTEGER NOT NULL DEFAULT 0,
              is_missing    INTEGER NOT NULL DEFAULT 0,
              sidecar_rel   TEXT,
              sidecar_mtime TEXT,
              tag_count     INTEGER NOT NULL DEFAULT 0,
              tagged_at     TEXT,
              thumb_rel     TEXT,
              rating        TEXT
            );
            CREATE TABLE IF NOT EXISTS tags (
              id           INTEGER PRIMARY KEY,
              name         TEXT NOT NULL UNIQUE,
              category     TEXT NOT NULL DEFAULT 'general',
              use_count    INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS media_tags (
              media_id     INTEGER NOT NULL REFERENCES media(id) ON DELETE CASCADE,
              tag_id       INTEGER NOT NULL REFERENCES tags(id),
              source       TEXT NOT NULL,
              model_id     TEXT,
              confidence   REAL,
              PRIMARY KEY (media_id, tag_id, source)
            );
            CREATE INDEX IF NOT EXISTS ix_media_taken ON media(taken_at);
            CREATE INDEX IF NOT EXISTS ix_media_kind ON media(kind);
            CREATE INDEX IF NOT EXISTS ix_media_rel ON media(rel_path);
            CREATE INDEX IF NOT EXISTS ix_mt_tag ON media_tags(tag_id);
            """;
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await EnsureColumnAsync("media", "tag_error", "TEXT", cancellationToken).ConfigureAwait(false);
        await SetMetaAsync("schema_version", "2", cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task UpsertMediaAsync(IReadOnlyList<MediaItem> items, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
        await using var tx = await _connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var cmd = _connection.CreateCommand();
            cmd.Transaction = (SqliteTransaction)tx;
            cmd.CommandText = """
                INSERT INTO media (
                  rel_path, file_name, ext, kind, byte_size, width, height, duration_ms, content_hash,
                  mtime_utc, taken_at, added_at, is_favorite, is_missing, sidecar_rel, tag_count, tagged_at, thumb_rel, rating)
                VALUES (
                  $rel, $name, $ext, $kind, $size, $w, $h, $dur, $hash,
                  $mtime, $taken, $added, 0, 0, $sidecar, $tags, $tagged, $thumb, $rating)
                ON CONFLICT(rel_path) DO UPDATE SET
                  file_name = excluded.file_name,
                  ext = excluded.ext,
                  kind = excluded.kind,
                  byte_size = excluded.byte_size,
                  width = COALESCE(excluded.width, width),
                  height = COALESCE(excluded.height, height),
                  mtime_utc = excluded.mtime_utc,
                  sidecar_rel = excluded.sidecar_rel,
                  thumb_rel = excluded.thumb_rel,
                  is_missing = 0;
                """;
            cmd.Parameters.AddWithValue("$rel", item.RelPath);
            cmd.Parameters.AddWithValue("$name", item.FileName);
            cmd.Parameters.AddWithValue("$ext", item.Ext);
            cmd.Parameters.AddWithValue("$kind", item.Kind.ToString().ToLowerInvariant());
            cmd.Parameters.AddWithValue("$size", item.ByteSize);
            cmd.Parameters.AddWithValue("$w", (object?)item.Width ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$h", (object?)item.Height ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$dur", (object?)item.DurationMs ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$hash", (object?)item.ContentHash ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$mtime", item.MtimeUtc.ToString("O"));
            cmd.Parameters.AddWithValue("$taken", (object?)item.TakenAt?.ToString("O") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$added", item.AddedAt.ToString("O"));
            cmd.Parameters.AddWithValue("$sidecar", (object?)item.SidecarRel ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$tags", item.TagCount);
            cmd.Parameters.AddWithValue("$tagged", (object?)item.TaggedAt?.ToString("O") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$thumb", (object?)item.ThumbRel ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$rating", (object?)item.Rating ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            if (item.SidecarRel is not null)
            {
                await ImportSidecarAsync(item, (SqliteTransaction)tx, cancellationToken).ConfigureAwait(false);
            }
        }

        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        await SetMetaAsync("last_scan_at", DateTimeOffset.UtcNow.ToString("O"), cancellationToken).ConfigureAwait(false);
        await RecalcUseCountsAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<IReadOnlyList<MediaItem>> QueryAsync(MediaQuery query, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
        var where = new List<string> { "is_missing = 0" };
        if (query.Section == RailSection.Favorites)
        {
            where.Add("is_favorite = 1");
        }

        if (query.Kind == MediaKindFilter.Images)
        {
            where.Add("kind = 'image'");
        }
        else if (query.Kind == MediaKindFilter.Videos)
        {
            where.Add("kind = 'video'");
        }

        if (query.Tagged == TaggedFilter.Tagged)
        {
            where.Add("tag_count > 0");
        }
        else if (query.Tagged == TaggedFilter.Untagged)
        {
            where.Add("tag_count = 0");
        }
        else if (query.Tagged == TaggedFilter.Failed)
        {
            where.Add("tag_error IS NOT NULL AND tag_error != ''");
        }

        if (query.Extensions.Count > 0)
        {
            var extList = string.Join(',', query.Extensions.Select(e => "'" + e.Replace("'", "''") + "'"));
            where.Add($"ext IN ({extList})");
        }

        foreach (var clause in query.Tags)
        {
            var name = clause.Name.Replace("'", "''");
            if (clause.Exclude)
            {
                where.Add($"id NOT IN (SELECT media_id FROM media_tags mt JOIN tags t ON t.id = mt.tag_id WHERE t.name = '{name}')");
            }
            else
            {
                where.Add($"id IN (SELECT media_id FROM media_tags mt JOIN tags t ON t.id = mt.tag_id WHERE t.name = '{name}')");
            }
        }

        var order = query.Sort switch
        {
            SortKey.Name => "file_name",
            SortKey.Size => "byte_size",
            SortKey.Type => "kind",
            SortKey.TagCount => "tag_count",
            SortKey.DateAdded => "added_at",
            SortKey.DateModified => "mtime_utc",
            _ => "COALESCE(taken_at, mtime_utc)"
        };
        var dir = query.Direction == SortDir.Asc ? "ASC" : "DESC";

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"SELECT * FROM media WHERE {string.Join(" AND ", where)} ORDER BY {order} {dir}, file_name ASC";
        var list = new List<MediaItem>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(ReadMedia(reader));
        }

        return list;
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<LibraryInfo> GetInfoAsync(CancellationToken cancellationToken = default)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM media WHERE is_missing = 0";
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        var last = await GetMetaAsync("last_scan_at", cancellationToken).ConfigureAwait(false);
        return new LibraryInfo
        {
            RootPath = RootPath,
            GeneratedFolderName = _generatedFolderName,
            LastScanAt = last is null ? null : DateTimeOffset.Parse(last),
            FileCount = count,
            IsOffline = !Directory.Exists(RootPath)
        };
    }

    public async Task SetFavoriteAsync(long id, bool isFavorite, CancellationToken cancellationToken = default)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "UPDATE media SET is_favorite = $fav WHERE id = $id";
        cmd.Parameters.AddWithValue("$fav", isFavorite ? 1 : 0);
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetDimensionsAsync(string relPath, int width, int height, CancellationToken cancellationToken = default)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "UPDATE media SET width = $w, height = $h WHERE rel_path = $rel";
        cmd.Parameters.AddWithValue("$w", width);
        cmd.Parameters.AddWithValue("$h", height);
        cmd.Parameters.AddWithValue("$rel", relPath);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> GetTagsAsync(long mediaId, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT DISTINCT t.name FROM tags t
                JOIN media_tags mt ON mt.tag_id = t.id
                WHERE mt.media_id = $id
                ORDER BY t.name;
                """;
            cmd.Parameters.AddWithValue("$id", mediaId);
            var tags = new List<string>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                tags.Add(reader.GetString(0));
            }

            return tags;
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task SetTagsAsync(long mediaId, IReadOnlyList<string> tags, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
        await using var tx = await _connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var del = _connection.CreateCommand())
        {
            del.Transaction = (SqliteTransaction)tx;
            del.CommandText = "DELETE FROM media_tags WHERE media_id = $id";
            del.Parameters.AddWithValue("$id", mediaId);
            await del.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var tag in tags)
        {
            var tagId = await EnsureTagAsync(tag, (SqliteTransaction)tx, cancellationToken).ConfigureAwait(false);
            await using var ins = _connection.CreateCommand();
            ins.Transaction = (SqliteTransaction)tx;
            ins.CommandText = "INSERT OR IGNORE INTO media_tags (media_id, tag_id, source) VALUES ($m, $t, 'sidecar')";
            ins.Parameters.AddWithValue("$m", mediaId);
            ins.Parameters.AddWithValue("$t", tagId);
            await ins.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        string? relPath;
        await using (var relCmd = _connection.CreateCommand())
        {
            relCmd.Transaction = (SqliteTransaction)tx;
            relCmd.CommandText = "SELECT rel_path FROM media WHERE id = $id";
            relCmd.Parameters.AddWithValue("$id", mediaId);
            relPath = await relCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        }

        await using (var upd = _connection.CreateCommand())
        {
            upd.Transaction = (SqliteTransaction)tx;
            upd.CommandText = """
                UPDATE media SET
                  tag_count = (SELECT COUNT(DISTINCT tag_id) FROM media_tags WHERE media_id = $id),
                  tagged_at = $now,
                  sidecar_rel = $sidecar
                WHERE id = $id;
                """;
            upd.Parameters.AddWithValue("$id", mediaId);
            upd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            upd.Parameters.AddWithValue("$sidecar", relPath is null ? DBNull.Value : SidecarFormat.SidecarRelative(relPath));
            await upd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        await RecalcUseCountsAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task ApplyAutoTagsAsync(long mediaId, IReadOnlyList<ScoredTag> tags, string? rating, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
        await using var tx = await _connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var del = _connection.CreateCommand())
        {
            del.Transaction = (SqliteTransaction)tx;
            del.CommandText = "DELETE FROM media_tags WHERE media_id = $id";
            del.Parameters.AddWithValue("$id", mediaId);
            await del.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var tag in tags)
        {
            var tagId = await EnsureTagAsync(tag.Name, (SqliteTransaction)tx, cancellationToken, tag.Category)
                .ConfigureAwait(false);
            await using var ins = _connection.CreateCommand();
            ins.Transaction = (SqliteTransaction)tx;
            ins.CommandText = """
                INSERT OR REPLACE INTO media_tags (media_id, tag_id, source, model_id, confidence)
                VALUES ($m, $t, 'model', $mid, $conf);
                """;
            ins.Parameters.AddWithValue("$m", mediaId);
            ins.Parameters.AddWithValue("$t", tagId);
            ins.Parameters.AddWithValue("$mid", (object?)tag.ModelId ?? DBNull.Value);
            ins.Parameters.AddWithValue("$conf", tag.Score);
            await ins.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        string? relPath;
        await using (var relCmd = _connection.CreateCommand())
        {
            relCmd.Transaction = (SqliteTransaction)tx;
            relCmd.CommandText = "SELECT rel_path FROM media WHERE id = $id";
            relCmd.Parameters.AddWithValue("$id", mediaId);
            relPath = await relCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        }

        await using (var upd = _connection.CreateCommand())
        {
            upd.Transaction = (SqliteTransaction)tx;
            upd.CommandText = """
                UPDATE media SET
                  tag_count = (SELECT COUNT(DISTINCT tag_id) FROM media_tags WHERE media_id = $id),
                  tagged_at = $now,
                  sidecar_rel = $sidecar,
                  rating = $rating,
                  tag_error = NULL
                WHERE id = $id;
                """;
            upd.Parameters.AddWithValue("$id", mediaId);
            upd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            upd.Parameters.AddWithValue("$sidecar", relPath is null ? DBNull.Value : SidecarFormat.SidecarRelative(relPath));
            upd.Parameters.AddWithValue("$rating", (object?)rating ?? DBNull.Value);
            await upd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        await RecalcUseCountsAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task SetTagErrorAsync(long mediaId, string? error, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "UPDATE media SET tag_error = $e WHERE id = $id";
        cmd.Parameters.AddWithValue("$e", (object?)error ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id", mediaId);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<IReadOnlyList<TagRecord>> SuggestTagsAsync(string? prefix, int limit = 20, CancellationToken cancellationToken = default)
    {
        await using var cmd = _connection.CreateCommand();
        if (string.IsNullOrWhiteSpace(prefix))
        {
            cmd.CommandText = "SELECT name, category, use_count FROM tags ORDER BY use_count DESC, name LIMIT $n";
        }
        else
        {
            cmd.CommandText = "SELECT name, category, use_count FROM tags WHERE name LIKE $p ORDER BY use_count DESC, name LIMIT $n";
            cmd.Parameters.AddWithValue("$p", prefix.Replace('_', ' ').ToLowerInvariant() + "%");
        }

        cmd.Parameters.AddWithValue("$n", limit);
        var list = new List<TagRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new TagRecord
            {
                Name = reader.GetString(0),
                Category = reader.GetString(1),
                UseCount = reader.GetInt32(2)
            });
        }

        return list;
    }

    public async Task MarkMissingExceptAsync(IReadOnlyCollection<string> presentRelPaths, CancellationToken cancellationToken = default)
    {
        await using var tx = await _connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var all = _connection.CreateCommand())
        {
            all.Transaction = (SqliteTransaction)tx;
            all.CommandText = "UPDATE media SET is_missing = 1";
            await all.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var rel in presentRelPaths)
        {
            await using var cmd = _connection.CreateCommand();
            cmd.Transaction = (SqliteTransaction)tx;
            cmd.CommandText = "UPDATE media SET is_missing = 0 WHERE rel_path = $rel";
            cmd.Parameters.AddWithValue("$rel", rel);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task CheckpointAsync(CancellationToken cancellationToken = default)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await CheckpointAsync().ConfigureAwait(false);
        }
        catch
        {
            // Best-effort so a USB copy still gets a single seiri.db.
        }

        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    private MediaItem ReadMedia(SqliteDataReader reader)
    {
        return new MediaItem
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            LibraryRoot = RootPath,
            RelPath = reader.GetString(reader.GetOrdinal("rel_path")),
            FileName = reader.GetString(reader.GetOrdinal("file_name")),
            Ext = reader.GetString(reader.GetOrdinal("ext")),
            Kind = reader.GetString(reader.GetOrdinal("kind")) == "video" ? MediaKind.Video : MediaKind.Image,
            ByteSize = reader.GetInt64(reader.GetOrdinal("byte_size")),
            Width = reader.IsDBNull(reader.GetOrdinal("width")) ? null : reader.GetInt32(reader.GetOrdinal("width")),
            Height = reader.IsDBNull(reader.GetOrdinal("height")) ? null : reader.GetInt32(reader.GetOrdinal("height")),
            DurationMs = reader.IsDBNull(reader.GetOrdinal("duration_ms")) ? null : reader.GetInt64(reader.GetOrdinal("duration_ms")),
            ContentHash = reader.IsDBNull(reader.GetOrdinal("content_hash")) ? null : reader.GetString(reader.GetOrdinal("content_hash")),
            MtimeUtc = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("mtime_utc"))),
            TakenAt = reader.IsDBNull(reader.GetOrdinal("taken_at")) ? null : DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("taken_at"))),
            AddedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("added_at"))),
            IsFavorite = reader.GetInt32(reader.GetOrdinal("is_favorite")) != 0,
            IsMissing = reader.GetInt32(reader.GetOrdinal("is_missing")) != 0,
            SidecarRel = reader.IsDBNull(reader.GetOrdinal("sidecar_rel")) ? null : reader.GetString(reader.GetOrdinal("sidecar_rel")),
            TagCount = reader.GetInt32(reader.GetOrdinal("tag_count")),
            TaggedAt = reader.IsDBNull(reader.GetOrdinal("tagged_at")) ? null : DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("tagged_at"))),
            ThumbRel = reader.IsDBNull(reader.GetOrdinal("thumb_rel")) ? null : reader.GetString(reader.GetOrdinal("thumb_rel")),
            Rating = reader.IsDBNull(reader.GetOrdinal("rating")) ? null : reader.GetString(reader.GetOrdinal("rating")),
            TagError = HasColumn(reader, "tag_error") && !reader.IsDBNull(reader.GetOrdinal("tag_error"))
                ? reader.GetString(reader.GetOrdinal("tag_error"))
                : null
        };
    }

    private async Task ImportSidecarAsync(MediaItem item, SqliteTransaction tx, CancellationToken cancellationToken)
    {
        var full = GeneratedLayout.ToFullPath(RootPath, item.SidecarRel!);
        if (!File.Exists(full))
        {
            return;
        }

        var text = await File.ReadAllTextAsync(full, cancellationToken).ConfigureAwait(false);
        var tags = ParseTags(text);
        if (tags.Count == 0)
        {
            return;
        }

        await using var idCmd = _connection.CreateCommand();
        idCmd.Transaction = tx;
        idCmd.CommandText = "SELECT id FROM media WHERE rel_path = $rel";
        idCmd.Parameters.AddWithValue("$rel", item.RelPath);
        var mediaIdObj = await idCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (mediaIdObj is null or DBNull)
        {
            return;
        }

        var mediaId = Convert.ToInt64(mediaIdObj);
        await using var del = _connection.CreateCommand();
        del.Transaction = tx;
        del.CommandText = "DELETE FROM media_tags WHERE media_id = $id AND source = 'sidecar'";
        del.Parameters.AddWithValue("$id", mediaId);
        await del.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        foreach (var tag in tags)
        {
            var tagId = await EnsureTagAsync(tag, tx, cancellationToken).ConfigureAwait(false);
            await using var ins = _connection.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = """
                INSERT OR IGNORE INTO media_tags (media_id, tag_id, source)
                VALUES ($m, $t, 'sidecar');
                """;
            ins.Parameters.AddWithValue("$m", mediaId);
            ins.Parameters.AddWithValue("$t", tagId);
            await ins.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var count = _connection.CreateCommand();
        count.Transaction = tx;
        count.CommandText = """
            UPDATE media SET
              tag_count = (SELECT COUNT(DISTINCT tag_id) FROM media_tags WHERE media_id = $id),
              tagged_at = $now
            WHERE id = $id;
            """;
        count.Parameters.AddWithValue("$id", mediaId);
        count.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await count.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<long> EnsureTagAsync(string name, SqliteTransaction? tx, CancellationToken cancellationToken, string category = "general")
    {
        await using var ins = _connection.CreateCommand();
        if (tx is not null)
        {
            ins.Transaction = tx;
        }

        ins.CommandText = "INSERT OR IGNORE INTO tags (name, category) VALUES ($n, $c)";
        ins.Parameters.AddWithValue("$n", name);
        ins.Parameters.AddWithValue("$c", string.IsNullOrWhiteSpace(category) ? "general" : category);
        await ins.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        await using var sel = _connection.CreateCommand();
        if (tx is not null)
        {
            sel.Transaction = tx;
        }

        sel.CommandText = "SELECT id FROM tags WHERE name = $n";
        sel.Parameters.AddWithValue("$n", name);
        var id = await sel.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt64(id);
    }

    private async Task RecalcUseCountsAsync(CancellationToken cancellationToken)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "UPDATE tags SET use_count = (SELECT COUNT(*) FROM media_tags WHERE tag_id = tags.id)";
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static List<string> ParseTags(string text)
    {
        var parts = text.Replace('\n', ',').Replace('\r', ',').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var tags = new List<string>();
        foreach (var part in parts)
        {
            var name = part.Replace('_', ' ').Trim().ToLowerInvariant();
            if (name.Length > 0 && !tags.Contains(name))
            {
                tags.Add(name);
            }
        }

        return tags;
    }

    private async Task SetMetaAsync(string key, string value, CancellationToken cancellationToken)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "INSERT INTO library_meta(key, value) VALUES ($k, $v) ON CONFLICT(key) DO UPDATE SET value = excluded.value";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> GetMetaAsync(string key, CancellationToken cancellationToken)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM library_meta WHERE key = $k";
        cmd.Parameters.AddWithValue("$k", key);
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result as string;
    }

    private async Task EnsureColumnAsync(string table, string column, string type, CancellationToken cancellationToken)
    {
        await using var info = _connection.CreateCommand();
        info.CommandText = $"PRAGMA table_info({table})";
        await using var reader = await info.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (reader.GetString(1).Equals(column, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        await using var alter = _connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type}";
        await alter.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static bool HasColumn(SqliteDataReader reader, string name)
    {
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (reader.GetName(i).Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
