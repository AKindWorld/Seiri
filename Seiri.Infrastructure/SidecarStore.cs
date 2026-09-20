using Seiri.Core;
using Seiri.Core.Models;

namespace Seiri.Infrastructure;

public static class SidecarStore
{
    public static void Write(MediaItem item, IReadOnlyList<string> tags, AppSettings settings) =>
        Write(item, tags.Select(t => new TagRecord { Name = t, Category = "general" }).ToList(), settings);

    public static void Write(MediaItem item, IReadOnlyList<TagRecord> tags, AppSettings settings, string? rating = null)
    {
        var rel = item.SidecarRel ?? SidecarFormat.SidecarRelative(item.RelPath);
        var path = GeneratedLayout.ToFullPath(item.LibraryRoot, rel);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var body = SidecarFormat.Format(tags, settings, rating);
        AtomicWrite(path, body);
    }

    public static void Write(MediaItem item, IReadOnlyList<ScoredTag> tags, string? rating, AppSettings settings)
    {
        var records = tags
            .Select(t => new TagRecord
            {
                Name = t.Name,
                Category = string.IsNullOrWhiteSpace(t.Category) ? "general" : t.Category
            })
            .ToList();
        Write(item, records, settings, rating);
    }

    public static void AtomicWrite(string path, string body)
    {
        var tmp = path + ".tmp";
        try
        {
            File.WriteAllText(tmp, body);
            using (var stream = new FileStream(tmp, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                stream.Flush(flushToDisk: true);
            }

            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(tmp))
                {
                    File.Delete(tmp);
                }
            }
            catch (Exception ex)
            {
                AppLog.Error($"sidecar tmp {tmp}", ex);
            }

            throw;
        }
    }
}
