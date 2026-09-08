using Seiri.Core;
using Seiri.Core.Models;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;

namespace Seiri.Services;

public sealed class ThumbnailGenerator
{
    public async Task GenerateAsync(string libraryRoot, string mediaRelUnix, CancellationToken cancellationToken = default)
    {
        var source = GeneratedLayout.ToFullPath(libraryRoot, mediaRelUnix);
        if (!File.Exists(source))
        {
            return;
        }

        var kind = MediaExtensions.Classify(Path.GetExtension(source));
        if (kind == MediaKind.Video)
        {
            await GenerateVideoAsync(libraryRoot, mediaRelUnix, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (kind != MediaKind.Image)
        {
            return;
        }

        var thumbRel = GeneratedLayout.ThumbRelativeUnix(mediaRelUnix);
        var dest = GeneratedLayout.ToFullPath(libraryRoot, thumbRel);
        var destDir = Path.GetDirectoryName(dest);
        if (!string.IsNullOrEmpty(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        var file = await StorageFile.GetFileFromPathAsync(source);
        using var stream = await file.OpenAsync(FileAccessMode.Read);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var width = decoder.PixelWidth;
        var height = decoder.PixelHeight;

        if (File.Exists(dest) && new FileInfo(dest).Length > 0
            && File.GetLastWriteTimeUtc(dest) >= File.GetLastWriteTimeUtc(source)
            && !await IsStaleSquareThumbAsync(dest, width, height))
        {
            return;
        }

        var longEdge = Math.Max(width, height);
        var outLong = Math.Min(512u, longEdge);
        var scale = outLong / (double)longEdge;
        var scaledW = Math.Max(1u, (uint)Math.Round(width * scale));
        var scaledH = Math.Max(1u, (uint)Math.Round(height * scale));

        var transform = new BitmapTransform
        {
            ScaledWidth = scaledW,
            ScaledHeight = scaledH,
            InterpolationMode = BitmapInterpolationMode.Fant
        };

        var pixelData = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            transform,
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.DoNotColorManage);

        var tmp = dest + ".tmp";
        if (File.Exists(tmp))
        {
            File.Delete(tmp);
        }

        using (var memory = new InMemoryRandomAccessStream())
        {
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, memory);
            encoder.SetPixelData(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                scaledW,
                scaledH,
                decoder.DpiX,
                decoder.DpiY,
                pixelData.DetachPixelData());
            await encoder.FlushAsync();
            memory.Seek(0);
            using var reader = new DataReader(memory.GetInputStreamAt(0));
            await reader.LoadAsync((uint)memory.Size);
            var bytes = new byte[memory.Size];
            reader.ReadBytes(bytes);
            await File.WriteAllBytesAsync(tmp, bytes, cancellationToken);
        }

        File.Copy(tmp, dest, overwrite: true);
        File.Delete(tmp);
    }

    private static async Task GenerateVideoAsync(string libraryRoot, string mediaRelUnix, CancellationToken cancellationToken)
    {
        var source = GeneratedLayout.ToFullPath(libraryRoot, mediaRelUnix);
        var dest = GeneratedLayout.ToFullPath(libraryRoot, GeneratedLayout.ThumbRelativeUnix(mediaRelUnix));
        var destDir = Path.GetDirectoryName(dest);
        if (!string.IsNullOrEmpty(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        if (File.Exists(dest) && new FileInfo(dest).Length > 0
            && File.GetLastWriteTimeUtc(dest) >= File.GetLastWriteTimeUtc(source))
        {
            return;
        }

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(source);
            using var thumb = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, 512, ThumbnailOptions.ResizeThumbnail);
            if (thumb is null || thumb.Size == 0)
            {
                return;
            }

            var decoder = await BitmapDecoder.CreateAsync(thumb);
            var pixel = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                new BitmapTransform(),
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.DoNotColorManage);
            var tmp = dest + ".tmp";
            using (var memory = new InMemoryRandomAccessStream())
            {
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, memory);
                encoder.SetPixelData(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied,
                    decoder.PixelWidth,
                    decoder.PixelHeight,
                    decoder.DpiX,
                    decoder.DpiY,
                    pixel.DetachPixelData());
                await encoder.FlushAsync();
                memory.Seek(0);
                using var reader = new DataReader(memory.GetInputStreamAt(0));
                await reader.LoadAsync((uint)memory.Size);
                var bytes = new byte[memory.Size];
                reader.ReadBytes(bytes);
                await File.WriteAllBytesAsync(tmp, bytes, cancellationToken);
            }

            File.Copy(tmp, dest, overwrite: true);
            File.Delete(tmp);
        }
        catch
        {
            // Some containers have no poster frame; the tile still shows a video mark.
        }
    }

    private static async Task<bool> IsStaleSquareThumbAsync(string dest, uint sourceWidth, uint sourceHeight)
    {
        if (sourceWidth == sourceHeight)
        {
            return false;
        }

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(dest);
            using var stream = await file.OpenAsync(FileAccessMode.Read);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            return decoder.PixelWidth == decoder.PixelHeight;
        }
        catch
        {
            return true;
        }
    }
}
