using Seiri.Core.Models;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Seiri.Services;

public static class ImagePixelLoader
{
    public static async Task<TaggerImage> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = await FileRandomAccessStream.OpenAsync(path, FileAccessMode.Read);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var pixel = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Straight,
            new BitmapTransform(),
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.DoNotColorManage);
        cancellationToken.ThrowIfCancellationRequested();
        var bgra = pixel.DetachPixelData();
        var width = (int)decoder.OrientedPixelWidth;
        var height = (int)decoder.OrientedPixelHeight;
        if (width <= 0 || height <= 0 || bgra.Length < width * height * 4)
        {
            width = (int)decoder.PixelWidth;
            height = (int)decoder.PixelHeight;
        }

        if (width <= 0 || height <= 0 || bgra.Length < width * height * 4)
        {
            throw new InvalidDataException($"Could not decode pixels for '{path}' ({bgra.Length} bytes).");
        }

        return new TaggerImage
        {
            Bgra = bgra,
            Width = width,
            Height = height
        };
    }
}
