using System.Runtime.CompilerServices;
using Seiri.Core;
using Seiri.Core.Contracts;
using Seiri.Core.Models;

namespace Seiri.Infrastructure;

public sealed class FileMediaScanner : IMediaScanner
{
    public async IAsyncEnumerable<MediaItem> ScanAsync(
        string libraryRoot,
        string generatedFolderName,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var root = LibraryNesting.Normalize(libraryRoot);
        var now = DateTimeOffset.UtcNow;

        IEnumerable<FileInfo> files;
        try
        {
            files = EnumerateMedia(root, generatedFolderName);
        }
        catch (Exception ex)
        {
            AppLog.Error($"enumerate {root}", ex);
            yield break;
        }

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MediaItem? item = null;
            try
            {
                MediaKind? kind = MediaExtensions.Classify(file.Extension);
                if (kind is null)
                {
                    continue;
                }

                var rel = GeneratedLayout.ToRelativeUnix(root, file.FullName);
                var size = kind == MediaKind.Image ? ImageDimensions.TryRead(file.FullName) : null;
                item = new MediaItem
                {
                    LibraryRoot = root,
                    RelPath = rel,
                    FileName = file.Name,
                    Ext = file.Extension.TrimStart('.').ToLowerInvariant(),
                    Kind = kind.Value,
                    ByteSize = file.Length,
                    Width = size?.Width,
                    Height = size?.Height,
                    MtimeUtc = file.LastWriteTimeUtc,
                    AddedAt = now,
                    SidecarRel = FindSidecar(root, file),
                    ThumbRel = GeneratedLayout.ThumbRelativeUnix(rel)
                };
            }
            catch (Exception ex)
            {
                AppLog.Error($"scan {file.FullName}", ex);
            }

            if (item is not null)
            {
                yield return item;
            }
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private static IEnumerable<FileInfo> EnumerateMedia(string root, string generatedFolderName)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.System
        };

        var rootDir = new DirectoryInfo(root);
        foreach (var file in rootDir.EnumerateFiles("*", options))
        {
            if (IsInsideGenerated(root, generatedFolderName, file.FullName))
            {
                continue;
            }

            var skip = false;
            var dir = file.Directory;
            while (dir is not null && !dir.FullName.Equals(root, StringComparison.OrdinalIgnoreCase))
            {
                if (GeneratedLayout.IsGeneratedDirectoryName(dir.Name))
                {
                    skip = true;
                    break;
                }

                dir = dir.Parent;
            }

            if (skip)
            {
                continue;
            }

            yield return file;
        }
    }

    private static bool IsInsideGenerated(string root, string generatedFolderName, string fullPath)
    {
        var generated = GeneratedLayout.GetFolder(root, generatedFolderName);
        return fullPath.StartsWith(generated + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindSidecar(string libraryRoot, FileInfo media)
    {
        var sidecar = Path.ChangeExtension(media.FullName, ".txt");
        return File.Exists(sidecar) ? GeneratedLayout.ToRelativeUnix(libraryRoot, sidecar) : null;
    }
}
