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

            var index = await OpenAsync(root, cancellationToken).ConfigureAwait(false);
            infos.Add(await index.GetInfoAsync(cancellationToken).ConfigureAwait(false));
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
                await index.UpsertMediaAsync(batch, cancellationToken).ConfigureAwait(false);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            await index.UpsertMediaAsync(batch, cancellationToken).ConfigureAwait(false);
        }

        await index.MarkMissingExceptAsync(seen, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetDimensionsAsync(MediaItem item, int width, int height, CancellationToken cancellationToken = default)
    {
        if (_open.TryGetValue(item.LibraryRoot, out var index))
        {
            await index.SetDimensionsAsync(item.RelPath, width, height, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<string>> GetTagsAsync(MediaItem item, CancellationToken cancellationToken = default)
    {
        if (_open.TryGetValue(item.LibraryRoot, out var index))
        {
            return await index.GetTagsAsync(item.Id, cancellationToken).ConfigureAwait(false);
        }

        return [];
    }

    public async Task<MediaItem?> SetTagsAsync(MediaItem item, IReadOnlyList<string> tags, AppSettings settings, CancellationToken cancellationToken = default)
    {
        if (!_open.TryGetValue(item.LibraryRoot, out var index))
        {
            return item;
        }

        var normalized = tags
            .Select(t => t.Replace('_', ' ').Trim().ToLowerInvariant())
            .Where(t => t.Length > 0)
            .Distinct()
            .ToList();

        await index.SetTagsAsync(item.Id, normalized, cancellationToken).ConfigureAwait(false);
        SidecarStore.Write(item, normalized, settings);

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

        var names = tags
            .Select(t => t.Name.Replace('_', ' ').Trim().ToLowerInvariant())
            .Where(t => t.Length > 0)
            .Distinct()
            .ToList();

        SidecarStore.Write(item, names, settings);
        await index.ApplyAutoTagsAsync(item.Id, tags, rating, cancellationToken).ConfigureAwait(false);
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
            foreach (var tag in await index.SuggestTagsAsync(prefix, 30, cancellationToken).ConfigureAwait(false))
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

    public async Task<IReadOnlyList<MediaItem>> QueryAsync(MediaQuery query, CancellationToken cancellationToken = default)
    {
        IEnumerable<ILibraryIndex> sources = _open.Values;
        if (!string.IsNullOrEmpty(query.LibraryRoot) && _open.TryGetValue(query.LibraryRoot, out var one))
        {
            sources = [one];
        }

        var merged = new List<MediaItem>();
        foreach (var index in sources)
        {
            merged.AddRange(await index.QueryAsync(query, cancellationToken).ConfigureAwait(false));
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

    public async Task SetFavoriteAsync(MediaItem item, bool isFavorite, CancellationToken cancellationToken = default)
    {
        if (_open.TryGetValue(item.LibraryRoot, out var index))
        {
            await index.SetFavoriteAsync(item.Id, isFavorite, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var index in _open.Values)
        {
            await index.DisposeAsync().ConfigureAwait(false);
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
        await index.InitializeAsync(cancellationToken).ConfigureAwait(false);
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
