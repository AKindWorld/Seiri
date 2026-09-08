using Seiri.Core.Models;

namespace Seiri.Core;

public static class MediaExtensions
{
    public static readonly HashSet<string> Images = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".tif", ".tiff",
        ".jxl", ".avif", ".heic", ".heif"
    };

    public static readonly HashSet<string> Videos = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".webm", ".mov", ".avi", ".wmv", ".m4v"
    };

    public static MediaKind? Classify(string extension)
    {
        if (Images.Contains(extension))
        {
            return MediaKind.Image;
        }

        if (Videos.Contains(extension))
        {
            return MediaKind.Video;
        }

        return null;
    }
}
