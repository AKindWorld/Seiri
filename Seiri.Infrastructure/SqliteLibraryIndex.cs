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
            Pooling = false,
            DefaultTimeout = 5
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
            PRAGMA busy_timeout = 5000;
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
            CREATE INDEX IF NOT EXISTS ix_media_hash ON media(content_hash);
            CREATE INDEX IF NOT EXISTS ix_mt_tag ON media_tags(tag_id);
            CREATE TABLE IF NOT EXISTS embeddings (
              media_id INTEGER NOT NULL REFERENCES media(id) ON DELETE CASCADE,
              model_id TEXT NOT NULL,
              dim INTEGER NOT NULL,
              vector BLOB NOT NULL,
              PRIMARY KEY (media_id, model_id)
            );
            """;
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await EnsureColumnAsync("media", "tag_error", "TEXT", cancellationToken).ConfigureAwait(false);
        await EnsureColumnAsync("media", "color_bucket", "TEXT", cancellationToken).ConfigureAwait(false);
        await SetMetaAsync("schema_version", "4", cancellationToken).ConfigureAwait(false);
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
                  is_missing = 0,
                  content_hash = CASE
                    WHEN excluded.byte_size != media.byte_size OR excluded.mtime_utc != media.mtime_utc THEN NULL
                    ELSE COALESCE(excluded.content_hash, media.content_hash)
                  END,
                  color_bucket = CASE
                    WHEN excluded.mtime_utc != media.mtime_utc THEN NULL
                    ELSE media.color_bucket
                  END;
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
                try
                {
                    await ImportSidecarAsync(item, (SqliteTransaction)tx, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    AppLog.Error($"import sidecar {item.RelPath}", ex);
                }
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
        var where = BuildWhere(query);
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
        cmd.CommandText = $"SELECT * FROM media WHERE {where} ORDER BY {order} {dir}, file_name ASC";
        var list = new List<MediaItem>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (TryReadMedia(reader) is { } item)
            {
                list.Add(item);
            }
        }

        return list;
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<int> CountAsync(MediaQuery query, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM media WHERE {BuildWhere(query)}";
            var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return Convert.ToInt32(result);
        }
        finally
        {
            _mutex.Release();
        }
    }

    private static string BuildWhere(MediaQuery query)
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
            where.Add(TagClauseSql(clause));
        }

        var folders = query.Folders.Count > 0
            ? query.Folders
            : string.IsNullOrWhiteSpace(query.Folder) ? [] : [query.Folder];
        if (folders.Count > 0)
        {
            var parts = folders.Select(f =>
            {
                var folder = f.Replace("'", "''").ToLowerInvariant();
                return $"instr(lower('/' || replace(rel_path, '\\', '/') || '/'), '/{folder}/') > 0";
            });
            where.Add("(" + string.Join(" OR ", parts) + ")");
        }

        var dateExpr = query.DateField switch
        {
            DateField.Added => "date(added_at)",
            DateField.Modified => "date(mtime_utc)",
            _ => "date(COALESCE(taken_at, mtime_utc))"
        };
        if (query.DateExact is { } exact)
        {
            where.Add($"{dateExpr} = '{exact:yyyy-MM-dd}'");
        }

        if (query.DateAfter is { } after)
        {
            where.Add($"{dateExpr} > '{after:yyyy-MM-dd}'");
        }

        if (query.DateBefore is { } before)
        {
            where.Add($"{dateExpr} < '{before:yyyy-MM-dd}'");
        }

        if (query.Orientations.Count > 0)
        {
            var parts = new List<string>();
            foreach (var o in query.Orientations)
            {
                parts.Add(o.ToLowerInvariant() switch
                {
                    "landscape" => "(width IS NOT NULL AND height IS NOT NULL AND width > height)",
                    "portrait" => "(width IS NOT NULL AND height IS NOT NULL AND height > width)",
                    "square" => "(width IS NOT NULL AND height IS NOT NULL AND abs(width - height) <= 0.05 * max(width, height))",
                    _ => "0"
                });
            }

            where.Add("(" + string.Join(" OR ", parts) + ")");
        }

        if (query.Aspects.Count > 0)
        {
            var parts = new List<string>();
            foreach (var a in query.Aspects)
            {
                var bits = a.Replace('x', ':').Split(':');
                if (bits.Length != 2
                    || !double.TryParse(bits[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var aw)
                    || !double.TryParse(bits[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ah)
                    || aw <= 0 || ah <= 0)
                {
                    continue;
                }

                parts.Add($"(width IS NOT NULL AND height IS NOT NULL AND abs(width * {ah.ToString(System.Globalization.CultureInfo.InvariantCulture)} - height * {aw.ToString(System.Globalization.CultureInfo.InvariantCulture)}) <= 0.08 * max(width * {ah.ToString(System.Globalization.CultureInfo.InvariantCulture)}, height * {aw.ToString(System.Globalization.CultureInfo.InvariantCulture)}))");
            }

            if (parts.Count > 0)
            {
                where.Add("(" + string.Join(" OR ", parts) + ")");
            }
        }

        if (query.Colors.Count > 0)
        {
            var list = string.Join(',', query.Colors.Select(c => "'" + c.Replace("'", "''").ToLowerInvariant() + "'"));
            where.Add($"lower(ifnull(color_bucket, '')) IN ({list})");
        }

        return string.Join(" AND ", where);
    }

    private static string TagClauseSql(TagClause clause)
    {
        var name = clause.Name.Replace("'", "''");
        var category = string.IsNullOrWhiteSpace(clause.Category) || clause.Category == "tag"
            ? null
            : clause.Category.Replace("'", "''");

        var extra = clause.Aliases
            .Where(a => !string.IsNullOrWhiteSpace(a) && !a.Equals(clause.Name, StringComparison.OrdinalIgnoreCase))
            .Select(a => a.Replace("'", "''").ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var aliasSql = extra.Count == 0
            ? string.Empty
            : " OR t.name IN (" + string.Join(',', extra.Select(a => "'" + a + "'")) + ")";

        string match;
        if (category == "rating")
        {
            match = $"(LOWER(IFNULL(rating, '')) = '{name}' OR id IN (SELECT media_id FROM media_tags mt JOIN tags t ON t.id = mt.tag_id WHERE (t.name = '{name}'{aliasSql}) AND t.category = 'rating'))";
        }
        else if (category is not null)
        {
            match = $"id IN (SELECT media_id FROM media_tags mt JOIN tags t ON t.id = mt.tag_id WHERE (instr(t.name, '{name}') > 0{aliasSql}) AND t.category = '{category}')";
        }
        else
        {
            match = $"id IN (SELECT media_id FROM media_tags mt JOIN tags t ON t.id = mt.tag_id WHERE instr(t.name, '{name}') > 0{aliasSql})";
        }

        return clause.Exclude ? $"NOT ({match})" : match;
    }

    public Task<LibraryInfo> GetInfoAsync(CancellationToken cancellationToken = default) =>
        WithLockAsync(async () =>
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM media WHERE is_missing = 0";
            var count = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
            var last = await GetMetaAsync("last_scan_at", cancellationToken).ConfigureAwait(false);
            DateTimeOffset? scanned = null;
            if (last is not null && DateTimeOffset.TryParse(last, out var parsed))
            {
                scanned = parsed;
            }

            return new LibraryInfo
            {
                RootPath = RootPath,
                GeneratedFolderName = _generatedFolderName,
                LastScanAt = scanned,
                FileCount = count,
                IsOffline = !Directory.Exists(RootPath)
            };
        }, cancellationToken);

    public Task SetFavoriteAsync(long id, bool isFavorite, CancellationToken cancellationToken = default) =>
        WithLockAsync(async () =>
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = "UPDATE media SET is_favorite = $fav WHERE id = $id";
            cmd.Parameters.AddWithValue("$fav", isFavorite ? 1 : 0);
            cmd.Parameters.AddWithValue("$id", id);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

    public Task SetDimensionsAsync(string relPath, int width, int height, CancellationToken cancellationToken = default) =>
        WithLockAsync(async () =>
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = "UPDATE media SET width = $w, height = $h WHERE rel_path = $rel";
            cmd.Parameters.AddWithValue("$w", width);
            cmd.Parameters.AddWithValue("$h", height);
            cmd.Parameters.AddWithValue("$rel", relPath);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

    public Task SetColorBucketAsync(string relPath, string bucket, CancellationToken cancellationToken = default) =>
        WithLockAsync(async () =>
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = "UPDATE media SET color_bucket = $c WHERE rel_path = $rel";
            cmd.Parameters.AddWithValue("$c", bucket);
            cmd.Parameters.AddWithValue("$rel", relPath);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

    public Task SetContentHashAsync(long id, string hash, CancellationToken cancellationToken = default) =>
        WithLockAsync(async () =>
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = "UPDATE media SET content_hash = $h WHERE id = $id";
            cmd.Parameters.AddWithValue("$h", hash);
            cmd.Parameters.AddWithValue("$id", id);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

    public async Task<IReadOnlyList<MediaItem>> ListUnhashedAsync(CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT * FROM media
                WHERE is_missing = 0 AND (content_hash IS NULL OR content_hash = '')
                ORDER BY byte_size ASC;
                """;
            var list = new List<MediaItem>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (TryReadMedia(reader) is { } item)
                {
                    list.Add(item);
                }
            }

            return list;
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<IReadOnlyList<IReadOnlyList<MediaItem>>> ListDuplicateGroupsAsync(CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT * FROM media
                WHERE is_missing = 0
                  AND content_hash IS NOT NULL
                  AND content_hash != ''
                  AND content_hash IN (
                    SELECT content_hash FROM media
                    WHERE is_missing = 0 AND content_hash IS NOT NULL AND content_hash != ''
                    GROUP BY content_hash HAVING COUNT(*) > 1
                  )
                ORDER BY content_hash, file_name;
                """;
            var groups = new List<IReadOnlyList<MediaItem>>();
            List<MediaItem>? current = null;
            string? hash = null;
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var item = TryReadMedia(reader);
                if (item is null)
                {
                    continue;
                }

                if (hash is null || !string.Equals(hash, item.ContentHash, StringComparison.OrdinalIgnoreCase))
                {
                    if (current is { Count: > 1 })
                    {
                        groups.Add(current);
                    }

                    current = [];
                    hash = item.ContentHash;
                }

                current!.Add(item);
            }

            if (current is { Count: > 1 })
            {
                groups.Add(current);
            }

            return groups;
        }
        finally
        {
            _mutex.Release();
        }
    }

    public Task UpsertEmbeddingAsync(long mediaId, string modelId, float[] vector, CancellationToken cancellationToken = default) =>
        WithLockAsync(async () =>
        {
            var bytes = new byte[vector.Length * sizeof(float)];
            Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO embeddings (media_id, model_id, dim, vector)
                VALUES ($id, $model, $dim, $vec)
                ON CONFLICT(media_id, model_id) DO UPDATE SET dim = excluded.dim, vector = excluded.vector;
                """;
            cmd.Parameters.AddWithValue("$id", mediaId);
            cmd.Parameters.AddWithValue("$model", modelId);
            cmd.Parameters.AddWithValue("$dim", vector.Length);
            cmd.Parameters.AddWithValue("$vec", bytes);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

    public Task<float[]?> GetEmbeddingAsync(long mediaId, string modelId, CancellationToken cancellationToken = default) =>
        WithLockAsync(async () =>
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT vector FROM embeddings WHERE media_id = $id AND model_id = $model";
            cmd.Parameters.AddWithValue("$id", mediaId);
            cmd.Parameters.AddWithValue("$model", modelId);
            var value = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return value is byte[] bytes ? FromBlob(bytes) : null;
        }, cancellationToken);

    public async Task<IReadOnlyList<(MediaItem Item, float[] Vector)>> ListEmbeddingsAsync(string modelId, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT m.*, e.vector FROM embeddings e
                JOIN media m ON m.id = e.media_id
                WHERE e.model_id = $model AND m.is_missing = 0;
                """;
            cmd.Parameters.AddWithValue("$model", modelId);
            var list = new List<(MediaItem, float[])>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var vecOrd = reader.GetOrdinal("vector");
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var item = TryReadMedia(reader);
                if (item is null || reader.IsDBNull(vecOrd))
                {
                    continue;
                }

                list.Add((item, FromBlob((byte[])reader.GetValue(vecOrd))));
            }

            return list;
        }
        finally
        {
            _mutex.Release();
        }
    }

    private static float[] FromBlob(byte[] bytes)
    {
        var n = bytes.Length / sizeof(float);
        var vector = new float[n];
        Buffer.BlockCopy(bytes, 0, vector, 0, n * sizeof(float));
        return vector;
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

    public async Task<IReadOnlyList<TagRecord>> GetTagRecordsAsync(long mediaId, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT t.name, t.category, t.use_count
                FROM tags t
                JOIN media_tags mt ON mt.tag_id = t.id
                WHERE mt.media_id = $id
                GROUP BY t.id
                ORDER BY
                  CASE t.category
                    WHEN 'rating' THEN 0
                    WHEN 'character' THEN 1
                    WHEN 'copyright' THEN 2
                    ELSE 3
                  END,
                  t.name;
                """;
            cmd.Parameters.AddWithValue("$id", mediaId);
            var tags = new List<TagRecord>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                tags.Add(new TagRecord
                {
                    Name = reader.GetString(0),
                    Category = reader.IsDBNull(1) ? "general" : reader.GetString(1),
                    UseCount = reader.GetInt32(2)
                });
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
        var records = tags
            .Select(t => new TagRecord { Name = t, Category = "general" })
            .ToList();
        await SetTagsAsync(mediaId, records, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetTagsAsync(long mediaId, IReadOnlyList<TagRecord> tags, CancellationToken cancellationToken = default)
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
            ins.CommandText = "INSERT OR IGNORE INTO media_tags (media_id, tag_id, source) VALUES ($m, $t, 'sidecar')";
            ins.Parameters.AddWithValue("$m", mediaId);
            ins.Parameters.AddWithValue("$t", tagId);
            await ins.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var rating = tags.FirstOrDefault(t => t.Category == "rating")?.Name;

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
                  rating = $rating
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

    public async Task DeleteMediaAsync(long id, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM media WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await RecalcUseCountsAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task UpdatePathsAsync(long id, RenamePlan plan, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                UPDATE media
                SET rel_path = $rel, file_name = $name, sidecar_rel = $sidecar, thumb_rel = $thumb
                WHERE id = $id
                """;
            cmd.Parameters.AddWithValue("$rel", plan.RelPath);
            cmd.Parameters.AddWithValue("$name", plan.FileName);
            cmd.Parameters.AddWithValue("$sidecar", plan.SidecarRel);
            cmd.Parameters.AddWithValue("$thumb", plan.ThumbRel);
            cmd.Parameters.AddWithValue("$id", id);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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

    public Task<IReadOnlyList<TagRecord>> SuggestTagsAsync(string? prefix, int limit = 20, CancellationToken cancellationToken = default) =>
        WithLockAsync(async () =>
        {
            await using var cmd = _connection.CreateCommand();
            if (string.IsNullOrWhiteSpace(prefix))
            {
                cmd.CommandText = "SELECT name, category, use_count FROM tags ORDER BY use_count DESC, name LIMIT $n";
            }
            else
            {
                var needle = prefix.Replace('_', ' ').ToLowerInvariant();
                cmd.CommandText = """
                    SELECT name, category, use_count FROM tags
                    WHERE instr(name, $p) > 0
                    ORDER BY (name = $p) DESC, (instr(name, $p) = 1) DESC, use_count DESC, name
                    LIMIT $n
                    """;
                cmd.Parameters.AddWithValue("$p", needle);
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

            return (IReadOnlyList<TagRecord>)list;
        }, cancellationToken);

    public Task<IReadOnlyList<TagRecord>> ListTagsAsync(int limit = 5000, CancellationToken cancellationToken = default) =>
        WithLockAsync(async () =>
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT name, category, use_count FROM tags
                WHERE use_count > 0
                ORDER BY use_count DESC, name
                LIMIT $n;
                """;
            cmd.Parameters.AddWithValue("$n", Math.Clamp(limit, 1, 20_000));
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

            return (IReadOnlyList<TagRecord>)list;
        }, cancellationToken);

    public Task<IReadOnlyList<string>> ListExtensionsAsync(CancellationToken cancellationToken = default) =>
        WithLockAsync(async () =>
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT DISTINCT ext FROM media WHERE is_missing = 0 ORDER BY ext";
            var list = new List<string>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var ext = reader.GetString(0);
                if (!string.IsNullOrWhiteSpace(ext))
                {
                    list.Add(ext);
                }
            }

            return (IReadOnlyList<string>)list;
        }, cancellationToken);

    public Task<IReadOnlyList<string>> ListFolderNamesAsync(CancellationToken cancellationToken = default) =>
        WithLockAsync(async () =>
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT DISTINCT substr(
                  replace(rel_path, '\', '/'),
                  1,
                  instr(replace(rel_path, '\', '/'), '/') - 1)
                FROM media
                WHERE is_missing = 0 AND instr(replace(rel_path, '\', '/'), '/') > 0
                ORDER BY 1 COLLATE NOCASE
                LIMIT 200;
                """;
            var list = new List<string>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var name = reader.GetString(0);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    list.Add(name);
                }
            }

            return (IReadOnlyList<string>)list;
        }, cancellationToken);

    public Task MarkMissingExceptAsync(IReadOnlyCollection<string> presentRelPaths, CancellationToken cancellationToken = default) =>
        WithLockAsync(async () =>
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
        }, cancellationToken);

    public Task CheckpointAsync(CancellationToken cancellationToken = default) =>
        WithLockAsync(async () =>
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        try
        {
            await CheckpointAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLog.Error($"checkpoint {RootPath}", ex);
        }

        try
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLog.Error($"dispose index {RootPath}", ex);
        }
    }

    private async Task WithLockAsync(Func<Task> work, CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await work().ConfigureAwait(false);
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task<T> WithLockAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await work().ConfigureAwait(false);
        }
        finally
        {
            _mutex.Release();
        }
    }

    private MediaItem? TryReadMedia(SqliteDataReader reader)
    {
        try
        {
            return ReadMedia(reader);
        }
        catch (Exception ex)
        {
            AppLog.Error($"read media {RootPath}", ex);
            return null;
        }
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
            MtimeUtc = ReadTime(reader, "mtime_utc") ?? DateTimeOffset.UtcNow,
            TakenAt = ReadTime(reader, "taken_at"),
            AddedAt = ReadTime(reader, "added_at") ?? DateTimeOffset.UtcNow,
            IsFavorite = reader.GetInt32(reader.GetOrdinal("is_favorite")) != 0,
            IsMissing = reader.GetInt32(reader.GetOrdinal("is_missing")) != 0,
            SidecarRel = reader.IsDBNull(reader.GetOrdinal("sidecar_rel")) ? null : reader.GetString(reader.GetOrdinal("sidecar_rel")),
            TagCount = reader.GetInt32(reader.GetOrdinal("tag_count")),
            TaggedAt = ReadTime(reader, "tagged_at"),
            ThumbRel = reader.IsDBNull(reader.GetOrdinal("thumb_rel")) ? null : reader.GetString(reader.GetOrdinal("thumb_rel")),
            Rating = reader.IsDBNull(reader.GetOrdinal("rating")) ? null : reader.GetString(reader.GetOrdinal("rating")),
            TagError = HasColumn(reader, "tag_error") && !reader.IsDBNull(reader.GetOrdinal("tag_error"))
                ? reader.GetString(reader.GetOrdinal("tag_error"))
                : null,
            ColorBucket = HasColumn(reader, "color_bucket") && !reader.IsDBNull(reader.GetOrdinal("color_bucket"))
                ? reader.GetString(reader.GetOrdinal("color_bucket"))
                : null
        };
    }

    private static DateTimeOffset? ReadTime(SqliteDataReader reader, string column)
    {
        var ord = reader.GetOrdinal(column);
        if (reader.IsDBNull(ord))
        {
            return null;
        }

        return DateTimeOffset.TryParse(reader.GetString(ord), out var value) ? value : null;
    }

    private async Task ImportSidecarAsync(MediaItem item, SqliteTransaction tx, CancellationToken cancellationToken)
    {
        var full = GeneratedLayout.ToFullPath(RootPath, item.SidecarRel!);
        if (!File.Exists(full))
        {
            return;
        }

        string text;
        try
        {
            text = await File.ReadAllTextAsync(full, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLog.Error($"sidecar {full}", ex);
            return;
        }
        var tags = SidecarFormat.ParseRecords(text);
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
            var tagId = await EnsureTagAsync(tag.Name, tx, cancellationToken, tag.Category).ConfigureAwait(false);
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

        var rating = tags.FirstOrDefault(t => t.Category == "rating")?.Name;
        await using var count = _connection.CreateCommand();
        count.Transaction = tx;
        count.CommandText = """
            UPDATE media SET
              tag_count = (SELECT COUNT(DISTINCT tag_id) FROM media_tags WHERE media_id = $id),
              tagged_at = $now,
              rating = COALESCE($rating, rating)
            WHERE id = $id;
            """;
        count.Parameters.AddWithValue("$id", mediaId);
        count.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        count.Parameters.AddWithValue("$rating", (object?)rating ?? DBNull.Value);
        await count.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<long> EnsureTagAsync(string name, SqliteTransaction? tx, CancellationToken cancellationToken, string category = "general")
    {
        await using var ins = _connection.CreateCommand();
        if (tx is not null)
        {
            ins.Transaction = tx;
        }

        var cat = string.IsNullOrWhiteSpace(category) ? "general" : category.ToLowerInvariant();
        ins.CommandText = "INSERT OR IGNORE INTO tags (name, category) VALUES ($n, $c)";
        ins.Parameters.AddWithValue("$n", name);
        ins.Parameters.AddWithValue("$c", cat);
        await ins.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        if (cat != "general")
        {
            await using var promote = _connection.CreateCommand();
            if (tx is not null)
            {
                promote.Transaction = tx;
            }

            promote.CommandText = "UPDATE tags SET category = $c WHERE name = $n AND category = 'general'";
            promote.Parameters.AddWithValue("$c", cat);
            promote.Parameters.AddWithValue("$n", name);
            await promote.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var sel = _connection.CreateCommand();
        if (tx is not null)
        {
            sel.Transaction = tx;
        }

        sel.CommandText = "SELECT id FROM tags WHERE name = $n";
        sel.Parameters.AddWithValue("$n", name);
        var id = await sel.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (id is null or DBNull)
        {
            throw new InvalidOperationException($"Tag '{name}' was not inserted.");
        }

        return Convert.ToInt64(id);
    }

    private async Task RecalcUseCountsAsync(CancellationToken cancellationToken)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "UPDATE tags SET use_count = (SELECT COUNT(*) FROM media_tags WHERE tag_id = tags.id)";
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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
