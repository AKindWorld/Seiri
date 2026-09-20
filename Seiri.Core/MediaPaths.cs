using Seiri.Core.Models;

namespace Seiri.Core;

public readonly record struct RenamePlan(string RelPath, string FileName, string SidecarRel, string ThumbRel);

public static class MediaPaths
{
    public static string SidecarRelative(MediaItem item) =>
        string.IsNullOrEmpty(item.SidecarRel) ? SidecarFormat.SidecarRelative(item.RelPath) : item.SidecarRel;

    public static string ThumbRelative(MediaItem item) =>
        string.IsNullOrEmpty(item.ThumbRel) ? GeneratedLayout.ThumbRelativeUnix(item.RelPath) : item.ThumbRel;

    public static RenamePlan PlanRename(MediaItem item, string newStem)
    {
        var stem = (newStem ?? string.Empty).Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            stem = stem.Replace(c, '_');
        }

        if (stem.Length == 0)
        {
            throw new ArgumentException("Name is empty.", nameof(newStem));
        }

        var ext = Path.GetExtension(item.FileName);
        if (string.IsNullOrEmpty(ext))
        {
            ext = "." + item.Ext.TrimStart('.');
        }

        var fileName = stem + ext;
        var slash = item.RelPath.LastIndexOf('/');
        var rel = slash < 0 ? fileName : item.RelPath[..(slash + 1)] + fileName;
        return new RenamePlan(
            rel,
            fileName,
            SidecarFormat.SidecarRelative(rel),
            GeneratedLayout.ThumbRelativeUnix(rel));
    }
}
