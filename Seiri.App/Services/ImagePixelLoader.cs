using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Seiri.Core.Models;

namespace Seiri.Services;

public static class ImagePixelLoader
{
    public static Task<TaggerImage> LoadAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(() => LoadGdi(path, maxEdge: 0), cancellationToken);

    public static Task<TaggerImage> LoadScaledAsync(
        string path,
        uint maxEdge = 448,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => LoadGdi(path, (int)maxEdge), cancellationToken);

    /// <summary>
    /// GDI+ software decode. WinRT BitmapDecoder can use the GPU and TDRs
    /// when DirectML is tagging on the same adapter.
    /// </summary>
    private static TaggerImage LoadGdi(string path, int maxEdge)
    {
        try
        {
            using var src = new Bitmap(path);
            var w = src.Width;
            var h = src.Height;
            if (w <= 0 || h <= 0)
            {
                throw new InvalidDataException($"Could not decode '{path}'.");
            }

            var nw = w;
            var nh = h;
            if (maxEdge > 0)
            {
                var factor = Math.Min(1.0, maxEdge / (double)Math.Max(w, h));
                nw = Math.Max(1, (int)Math.Round(w * factor));
                nh = Math.Max(1, (int)Math.Round(h * factor));
            }

            using var dest = new Bitmap(nw, nh, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(dest))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(src, 0, 0, nw, nh);
            }

            var rect = new Rectangle(0, 0, nw, nh);
            var data = dest.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var bgra = new byte[nw * nh * 4];
                var stride = Math.Abs(data.Stride);
                for (var y = 0; y < nh; y++)
                {
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * stride), bgra, y * nw * 4, nw * 4);
                }

                return new TaggerImage { Bgra = bgra, Width = nw, Height = nh };
            }
            finally
            {
                dest.UnlockBits(data);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not InvalidDataException)
        {
            return LoadWinRt(path, maxEdge);
        }
    }

    private static TaggerImage LoadWinRt(string path, int maxEdge)
    {
        return ImagingWorker.RunAsync(async () =>
        {
            using var stream = await Windows.Storage.Streams.FileRandomAccessStream.OpenAsync(
                path, Windows.Storage.FileAccessMode.Read);
            var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
            var sourceW = decoder.OrientedPixelWidth;
            var sourceH = decoder.OrientedPixelHeight;
            if (sourceW == 0 || sourceH == 0)
            {
                sourceW = decoder.PixelWidth;
                sourceH = decoder.PixelHeight;
            }

            uint width = sourceW;
            uint height = sourceH;
            var transform = new Windows.Graphics.Imaging.BitmapTransform();
            if (maxEdge > 0)
            {
                var factor = Math.Min(1.0, maxEdge / (double)Math.Max(sourceW, sourceH));
                width = Math.Max(1u, (uint)Math.Round(sourceW * factor));
                height = Math.Max(1u, (uint)Math.Round(sourceH * factor));
                transform.ScaledWidth = width;
                transform.ScaledHeight = height;
                transform.InterpolationMode = Windows.Graphics.Imaging.BitmapInterpolationMode.Fant;
            }

            var pixel = await decoder.GetPixelDataAsync(
                Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
                Windows.Graphics.Imaging.BitmapAlphaMode.Straight,
                transform,
                Windows.Graphics.Imaging.ExifOrientationMode.RespectExifOrientation,
                Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage);
            var bgra = pixel.DetachPixelData();
            var w = (int)width;
            var h = (int)height;
            if (w <= 0 || h <= 0 || bgra.Length < w * h * 4)
            {
                throw new InvalidDataException($"Could not decode '{path}'.");
            }

            return new TaggerImage { Bgra = bgra, Width = w, Height = h };
        }).GetAwaiter().GetResult();
    }
}
