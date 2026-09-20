using Seiri.Core;
using Seiri.Core.Models;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;

namespace Seiri.Services;

public sealed class ThumbnailGenerator
{
    public async Task<string?> GenerateAsync(
        string libraryRoot,
        string mediaRelUnix,
        CancellationToken cancellationToken = default,
        bool verifyShape = true)
    {
        if (!ImagingWorker.HasAccess)
        {
            return await ImagingWorker.RunAsync(
                () => GenerateAsync(libraryRoot, mediaRelUnix, cancellationToken, verifyShape),
                cancellationToken).ConfigureAwait(false);
        }

        var source = GeneratedLayout.ToFullPath(libraryRoot, mediaRelUnix);
        if (!File.Exists(source))
        {
            return null;
        }

        try
        {
        var kind = MediaExtensions.Classify(Path.GetExtension(source));
        if (kind == MediaKind.Video)
        {
            return await GenerateVideoAsync(libraryRoot, mediaRelUnix, cancellationToken).ConfigureAwait(false);
        }

        if (kind != MediaKind.Image)
        {
            return null;
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
            && (!verifyShape || !await IsStaleSquareThumbAsync(dest, width, height)))
        {
            return await ColorFromFileAsync(dest);
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
        var pixels = pixelData.DetachPixelData();
        var bucket = ColorBucket.FromBgra(pixels, (int)scaledW, (int)scaledH);

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
                pixels);
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
        return bucket;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Error($"thumb {source}", ex);
            return null;
        }
    }

    public static async Task<string?> ColorFromFileAsync(string path)
    {
        if (!ImagingWorker.HasAccess)
        {
            return await ImagingWorker.RunAsync(() => ColorFromFileAsync(path)).ConfigureAwait(false);
        }

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenAsync(FileAccessMode.Read);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var pixel = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                new BitmapTransform(),
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.DoNotColorManage);
            return ColorBucket.FromBgra(pixel.DetachPixelData(), (int)decoder.PixelWidth, (int)decoder.PixelHeight);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> GenerateVideoAsync(string libraryRoot, string mediaRelUnix, CancellationToken cancellationToken)
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
            return await ColorFromFileAsync(dest);
        }

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(source);
            using var thumb = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, 512, ThumbnailOptions.ResizeThumbnail);
            if (thumb is null || thumb.Size == 0)
            {
                return null;
            }

            var decoder = await BitmapDecoder.CreateAsync(thumb);
            var pixel = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                new BitmapTransform(),
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.DoNotColorManage);
            var pixels = pixel.DetachPixelData();
            var bucket = ColorBucket.FromBgra(pixels, (int)decoder.PixelWidth, (int)decoder.PixelHeight);
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
                    pixels);
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
            return bucket;
        }
        catch
        {
            // Some containers have no poster frame; the tile still shows a video mark.
            return null;
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
