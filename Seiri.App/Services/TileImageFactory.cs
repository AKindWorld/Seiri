using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace Seiri.Services;

public static class TileImageFactory
{
    public static async Task<ImageSource?> CreateAsync(string path, int decodeWidth, bool hardware)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
        decodeWidth = Math.Clamp(decodeWidth, 64, 512);
        if (hardware)
        {
            var image = new BitmapImage();
            image.DecodePixelWidth = decodeWidth;
            image.UriSource = new Uri(path);
            return image;
        }

        var file = await StorageFile.GetFileFromPathAsync(path);
        using var stream = await file.OpenReadAsync();
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var w = decoder.PixelWidth;
        var h = decoder.PixelHeight;
        var scale = Math.Min(1, decodeWidth / (double)Math.Max(1, w));
        var sw = Math.Max(1u, (uint)Math.Round(w * scale));
        var sh = Math.Max(1u, (uint)Math.Round(h * scale));
        var software = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            new BitmapTransform
            {
                ScaledWidth = sw,
                ScaledHeight = sh,
                InterpolationMode = BitmapInterpolationMode.Fant
            },
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.DoNotColorManage);
        var source = new SoftwareBitmapSource();
        await source.SetBitmapAsync(software);
        return source;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
