using Seiri.Core;
using Seiri.Core.Models;

namespace Seiri.Infrastructure;

public static class SidecarStore
{
    public static void Write(MediaItem item, IReadOnlyList<string> tags, AppSettings settings)
    {
        var rel = item.SidecarRel ?? SidecarFormat.SidecarRelative(item.RelPath);
        var path = GeneratedLayout.ToFullPath(item.LibraryRoot, rel);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var body = SidecarFormat.Format(tags, settings);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, body);
        File.Copy(tmp, path, overwrite: true);
        File.Delete(tmp);
    }
}
