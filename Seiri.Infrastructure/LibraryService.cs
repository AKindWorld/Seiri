using Seiri.Core;
using Seiri.Core.Contracts;
using Seiri.Core.Models;

namespace Seiri.Infrastructure;

public sealed class LibraryService : IAsyncDisposable
{
    private readonly ILibraryRegistry _registry;
    private readonly IMediaScanner _scanner;
    private readonly Dictionary<string, ILibraryIndex> _open = new(StringComparer.OrdinalIgnoreCase);

    public LibraryService(ILibraryRegistry registry, IMediaScanner scanner)
    {
        _registry = registry;
        _scanner = scanner;
    }

    public IReadOnlyCollection<ILibraryIndex> OpenIndexes => _open.Values;
    public TagAliasCatalog? Aliases { get; set; }

    public async Task<IReadOnlyList<LibraryInfo>> LoadPersistedAsync(CancellationToken cancellationToken = default)
    {
        var infos = new List<LibraryInfo>();
        foreach (var root in _registry.GetRoots())
        {
            if (!Directory.Exists(root))
            {
                infos.Add(new LibraryInfo { RootPath = root, IsOffline = true });
                continue;
            }

            try
            {
                var index = await OpenAsync(root, cancellationToken).ConfigureAwait(false);
                infos.Add(await index.GetInfoAsync(cancellationToken).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                AppLog.Error($"open library {root}", ex);
                infos.Add(new LibraryInfo { RootPath = root, IsOffline = true });
            }
        }

        return infos;
    }

    public async Task<LibraryInfo> AddAsync(string path, CancellationToken cancellationToken = default)
    {
        var root = LibraryNesting.Normalize(path);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(root);
        }

        var existing = _registry.GetRoots();
        if (LibraryNesting.Conflicts(root, existing.Concat(_open.Keys)))
        {
            throw new InvalidOperationException("That folder is already in the library, or nested inside another library.");
        }

        var index = await OpenAsync(root, cancellationToken).ConfigureAwait(false);
        await ScanAsync(index, cancellationToken).ConfigureAwait(false);

        var roots = existing.ToList();
        if (!roots.Contains(root, StringComparer.OrdinalIgnoreCase))
        {
            roots.Add(root);
            _registry.Save(roots);
        }

        return await index.GetInfoAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(string path, bool deleteGenerated, CancellationToken cancellationToken = default)
    {
        var root = LibraryNesting.Normalize(path);
        if (_open.Remove(root, out var index))
        {
            await index.DisposeAsync().ConfigureAwait(false);
        }

        var roots = _registry.GetRoots().Where(r => !r.Equals(root, StringComparison.OrdinalIgnoreCase)).ToList();
        _registry.Save(roots);

        if (deleteGenerated)
        {
            var folder = GeneratedLayout.GetFolder(root);
            if (Directory.Exists(folder) && File.Exists(GeneratedLayout.GetMarkerPath(root)))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    public async Task ScanAsync(ILibraryIndex index, CancellationToken cancellationToken = default)
    {
        var batch = new List<MediaItem>(64);
        var seen = new List<string>();
        await foreach (var item in _scanner.ScanAsync(index.RootPath, index.GeneratedFolderName, cancellationToken).ConfigureAwait(false))
        {
            batch.Add(item);
            seen.Add(item.RelPath);
            if (batch.Count >= 64)
            {
                await FlushBatchAsync(index, batch, cancellationToken).ConfigureAwait(false);
            }
        }

        if (batch.Count > 0)
        {
            await FlushBatchAsync(index, batch, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await index.MarkMissingExceptAsync(seen, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLog.Error($"mark missing {index.RootPath}", ex);
        }
    }

    private static async Task FlushBatchAsync(ILibraryIndex index, List<MediaItem> batch, CancellationToken cancellationToken)
    {
        try
        {
            await index.UpsertMediaAsync(batch, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLog.Error($"upsert batch {index.RootPath} ({batch.Count} files)", ex);
        }

        batch.Clear();
    }

    public async Task SetDimensionsAsync(MediaItem item, int width, int height, CancellationToken cancellationToken = default)
    {
        if (_open.TryGetValue(item.LibraryRoot, out var index))
        {
            try
            {
                await index.SetDimensionsAsync(item.RelPath, width, height, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLog.Error($"set dimensions {item.FileName}", ex);
            }
        }
    }

    public async Task SetColorBucketAsync(MediaItem item, string bucket, CancellationToken cancellationToken = default)
    {
        if (_open.TryGetValue(item.LibraryRoot, out var index))
        {
            try
            {
                await index.SetColorBucketAsync(item.RelPath, bucket, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLog.Error($"set color {item.FileName}", ex);
            }
        }
    }

    public async Task SetContentHashAsync(MediaItem item, string hash, CancellationToken cancellationToken = default)
    {
        if (_open.TryGetValue(item.LibraryRoot, out var index))
        {
            try
            {
                await index.SetContentHashAsync(item.Id, hash, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLog.Error($"set hash {item.FileName}", ex);
            }
        }
    }

    public async Task<IReadOnlyList<MediaItem>> ListUnhashedAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<MediaItem>();
        foreach (var index in _open.Values)
        {
            try
            {
                list.AddRange(await index.ListUnhashedAsync(cancellationToken).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                AppLog.Error($"list unhashed {index.RootPath}", ex);
            }
        }

        return list;
    }

    public async Task<IReadOnlyList<IReadOnlyList<MediaItem>>> ListDuplicateGroupsAsync(CancellationToken cancellationToken = default)
    {
        var groups = new List<IReadOnlyList<MediaItem>>();
        foreach (var index in _open.Values)
        {
            try
            {
                groups.AddRange(await index.ListDuplicateGroupsAsync(cancellationToken).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                AppLog.Error($"list duplicates {index.RootPath}", ex);
            }
        }

        return groups;
    }

    public async Task UpsertEmbeddingAsync(MediaItem item, string modelId, float[] vector, CancellationToken cancellationToken = default)
    {
        if (_open.TryGetValue(item.LibraryRoot, out var index))
        {
            await index.UpsertEmbeddingAsync(item.Id, modelId, vector, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<float[]?> GetEmbeddingAsync(MediaItem item, string modelId, CancellationToken cancellationToken = default)
    {
        if (_open.TryGetValue(item.LibraryRoot, out var index))
        {
            return await index.GetEmbeddingAsync(item.Id, modelId, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    public async Task<IReadOnlyList<(MediaItem Item, float[] Vector)>> ListEmbeddingsAsync(string modelId, CancellationToken cancellationToken = default)
    {
        var list = new List<(MediaItem, float[])>();
        foreach (var index in _open.Values)
        {
            try
            {
                list.AddRange(await index.ListEmbeddingsAsync(modelId, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                AppLog.Error($"list embeddings {index.RootPath}", ex);
            }
        }

        return list;
    }

    public async Task<IReadOnlyList<string>> GetTagsAsync(MediaItem item, CancellationToken cancellationToken = default)
    {
        if (_open.TryGetValue(item.LibraryRoot, out var index))
        {
            try
            {
                return await index.GetTagsAsync(item.Id, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLog.Error($"get tags {item.FileName}", ex);
            }
        }

        return [];
    }

    public async Task<IReadOnlyList<TagRecord>> GetTagRecordsAsync(MediaItem item, CancellationToken cancellationToken = default)
    {
        if (_open.TryGetValue(item.LibraryRoot, out var index))
        {
            try
            {
                return await index.GetTagRecordsAsync(item.Id, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLog.Error($"get tag records {item.FileName}", ex);
            }
        }

        return [];
    }

    public Task<MediaItem?> SetTagsAsync(MediaItem item, IReadOnlyList<string> tags, AppSettings settings, CancellationToken cancellationToken = default)
    {
        var records = tags
            .Select(t => new TagRecord
            {
                Name = t.Replace('_', ' ').Trim().ToLowerInvariant(),
                Category = "general"
            })
            .Where(t => t.Name.Length > 0)
            .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
        return SetTagsAsync(item, records, settings, cancellationToken);
    }

    public async Task<MediaItem?> SetTagsAsync(MediaItem item, IReadOnlyList<TagRecord> tags, AppSettings settings, CancellationToken cancellationToken = default)
    {
        if (!_open.TryGetValue(item.LibraryRoot, out var index))
        {
            return item;
        }

        var normalized = tags
            .Select(t => new TagRecord
            {
                Name = t.Name.Replace('_', ' ').Trim().ToLowerInvariant(),
                Category = string.IsNullOrWhiteSpace(t.Category) ? "general" : t.Category.ToLowerInvariant()
            })
            .Where(t => t.Name.Length > 0)
            .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        var rating = normalized.FirstOrDefault(t => t.Category == "rating")?.Name;
        try
        {
            SidecarStore.Write(item, normalized, settings, rating);
        }
        catch (Exception ex)
        {
            AppLog.Error($"write sidecar {item.FullPath}", ex);
            throw;
        }

        try
        {
            await index.SetTagsAsync(item.Id, normalized, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLog.Error($"index tags after sidecar {item.FileName}", ex);
            throw;
        }

        var query = new MediaQuery { LibraryRoot = item.LibraryRoot };
        var refreshed = await QueryAsync(query, cancellationToken).ConfigureAwait(false);
        return refreshed.FirstOrDefault(m => m.Id == item.Id) ?? item;
    }

    public async Task ApplyAutoTagsAsync(MediaItem item, IReadOnlyList<ScoredTag> tags, string? rating, AppSettings settings, CancellationToken cancellationToken = default)
    {
        if (!_open.TryGetValue(item.LibraryRoot, out var index))
        {
            return;
        }

        try
        {
            SidecarStore.Write(item, tags, rating, settings);
        }
        catch (Exception ex)
        {
            AppLog.Error($"write sidecar {item.FullPath}", ex);
            throw;
        }

        try
        {
            await index.ApplyAutoTagsAsync(item.Id, tags, rating, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Sidecar is already durable; the next scan will import it.
            AppLog.Error($"index auto tags after sidecar {item.FileName}", ex);
        }
    }

    public async Task SetTagErrorAsync(MediaItem item, string? error, CancellationToken cancellationToken = default)
    {
        if (_open.TryGetValue(item.LibraryRoot, out var index))
        {
            await index.SetTagErrorAsync(item.Id, error, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<TagRecord>> SuggestTagsAsync(string? prefix, CancellationToken cancellationToken = default)
    {
        var merged = new Dictionary<string, TagRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var index in _open.Values)
        {
            IReadOnlyList<TagRecord> tags;
            try
            {
                tags = await index.SuggestTagsAsync(prefix, 30, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLog.Error($"suggest tags {index.RootPath}", ex);
                continue;
            }

            foreach (var tag in tags)
            {
                if (merged.TryGetValue(tag.Name, out var existing))
                {
                    merged[tag.Name] = new TagRecord
                    {
                        Name = tag.Name,
                        Category = tag.Category,
                        UseCount = existing.UseCount + tag.UseCount
                    };
                }
                else
                {
                    merged[tag.Name] = tag;
                }
            }
        }

        return merged.Values.OrderByDescending(t => t.UseCount).ThenBy(t => t.Name).Take(20).ToList();
    }

    public async Task<IReadOnlyList<TagRecord>> ListTagsAsync(CancellationToken cancellationToken = default)
    {
        var merged = new Dictionary<string, TagRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var index in _open.Values)
        {
            IReadOnlyList<TagRecord> tags;
            try
            {
                tags = await index.ListTagsAsync(5000, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLog.Error($"list tags {index.RootPath}", ex);
                continue;
            }

            foreach (var tag in tags)
            {
                var key = tag.Category + "\u001f" + tag.Name;
                if (merged.TryGetValue(key, out var existing))
                {
                    merged[key] = new TagRecord
                    {
                        Name = tag.Name,
                        Category = tag.Category,
                        UseCount = existing.UseCount + tag.UseCount
                    };
                }
                else
                {
                    merged[key] = tag;
                }
            }
        }

        return merged.Values.OrderByDescending(t => t.UseCount).ThenBy(t => t.Name).ToList();
    }

    public async Task<IReadOnlyList<string>> ListExtensionsAsync(CancellationToken cancellationToken = default)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var index in _open.Values)
        {
            IReadOnlyList<string> exts;
            try
            {
                exts = await index.ListExtensionsAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLog.Error($"list extensions {index.RootPath}", ex);
                continue;
            }

            foreach (var ext in exts)
            {
                set.Add(ext);
            }
        }

        return set.OrderBy(e => e, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<IReadOnlyList<string>> ListFolderNamesAsync(CancellationToken cancellationToken = default)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var index in _open.Values)
        {
            var lib = Path.GetFileName(index.RootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!string.IsNullOrWhiteSpace(lib))
            {
                names.Add(lib);
            }

            IReadOnlyList<string> folders;
            try
            {
                folders = await index.ListFolderNamesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLog.Error($"list folders {index.RootPath}", ex);
                continue;
            }

            foreach (var folder in folders)
            {
                names.Add(folder);
            }
        }

        return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static MediaQuery BindFolder(ILibraryIndex index, MediaQuery query)
    {
        var folders = query.Folders.Count > 0
            ? query.Folders
            : string.IsNullOrWhiteSpace(query.Folder) ? [] : new List<string> { query.Folder };
        if (folders.Count == 0)
        {
            return query;
        }

        var lib = Path.GetFileName(index.RootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (folders.Any(f => lib.Equals(f, StringComparison.OrdinalIgnoreCase)))
        {
            var clone = query.Clone();
            clone.Folder = null;
            clone.Folders = [];
            return clone;
        }

        return query;
    }

    public async Task<IReadOnlyList<MediaItem>> QueryAsync(MediaQuery query, CancellationToken cancellationToken = default)
    {
        query = Aliases?.ExpandQuery(query) ?? query;
        IEnumerable<ILibraryIndex> sources = _open.Values;
        if (!string.IsNullOrEmpty(query.LibraryRoot) && _open.TryGetValue(query.LibraryRoot, out var one))
        {
            sources = [one];
        }

        var merged = new List<MediaItem>();
        foreach (var index in sources)
        {
            try
            {
                merged.AddRange(await index.QueryAsync(BindFolder(index, query), cancellationToken).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                AppLog.Error($"query {index.RootPath}", ex);
            }
        }

        IOrderedEnumerable<MediaItem> ordered = query.Sort switch
        {
            SortKey.Name => merged.OrderBy(m => m.FileName, StringComparer.CurrentCultureIgnoreCase),
            SortKey.Size => merged.OrderBy(m => m.ByteSize),
            SortKey.Type => merged.OrderBy(m => m.Kind).ThenBy(m => m.FileName, StringComparer.CurrentCultureIgnoreCase),
            SortKey.TagCount => merged.OrderBy(m => m.TagCount),
            SortKey.DateAdded => merged.OrderBy(m => m.AddedAt),
            SortKey.DateModified => merged.OrderBy(m => m.MtimeUtc),
            _ => merged.OrderBy(m => m.SortDate)
        };

        var list = (query.Direction == SortDir.Desc ? ordered.Reverse() : ordered).ToList();
        return list;
    }

    public async Task<int> CountAsync(MediaQuery query, CancellationToken cancellationToken = default)
    {
        query = Aliases?.ExpandQuery(query) ?? query;
        IEnumerable<ILibraryIndex> sources = _open.Values;
        if (!string.IsNullOrEmpty(query.LibraryRoot) && _open.TryGetValue(query.LibraryRoot, out var one))
        {
            sources = [one];
        }

        var total = 0;
        foreach (var index in sources)
        {
            try
            {
                total += await index.CountAsync(BindFolder(index, query), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLog.Error($"count {index.RootPath}", ex);
            }
        }

        return total;
    }

    public async Task SetFavoriteAsync(MediaItem item, bool isFavorite, CancellationToken cancellationToken = default)
    {
        if (_open.TryGetValue(item.LibraryRoot, out var index))
        {
            await index.SetFavoriteAsync(item.Id, isFavorite, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task DeleteItemAsync(MediaItem item, CancellationToken cancellationToken = default)
    {
        if (_open.TryGetValue(item.LibraryRoot, out var index))
        {
            await index.DeleteMediaAsync(item.Id, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task RenameItemAsync(MediaItem item, RenamePlan plan, CancellationToken cancellationToken = default)
    {
        if (_open.TryGetValue(item.LibraryRoot, out var index))
        {
            await index.UpdatePathsAsync(item.Id, plan, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var index in _open.Values)
        {
            try
            {
                await index.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLog.Error($"dispose {index.RootPath}", ex);
            }
        }

        _open.Clear();
    }

    private async Task<ILibraryIndex> OpenAsync(string root, CancellationToken cancellationToken)
    {
        if (_open.TryGetValue(root, out var existing))
        {
            return existing;
        }

        var folderName = ResolveGeneratedFolder(root);
        var folder = GeneratedLayout.GetFolder(root, folderName);
        Directory.CreateDirectory(folder);
        var marker = GeneratedLayout.GetMarkerPath(root, folderName);
        if (!File.Exists(marker))
        {
            await File.WriteAllTextAsync(marker, "seiri-library", cancellationToken).ConfigureAwait(false);
        }

        var index = new SqliteLibraryIndex(root, folderName);
        try
        {
            await index.InitializeAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await index.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        _open[root] = index;
        return index;
    }

    private static string ResolveGeneratedFolder(string root)
    {
        var preferred = GeneratedLayout.GetFolder(root);
        var marker = GeneratedLayout.GetMarkerPath(root);
        if (Directory.Exists(preferred) && !File.Exists(marker))
        {
            var hasForeign = Directory.EnumerateFileSystemEntries(preferred).Any();
            if (hasForeign)
            {
                return GeneratedLayout.FallbackFolderName;
            }
        }

        return GeneratedLayout.FolderName;
    }
}
