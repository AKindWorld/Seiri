namespace Seiri.Core.Tagging;

public static class ImageResize
{
    public static (byte[] Rgb, int Width, int Height) FlattenAndLimit(
        ReadOnlySpan<byte> bgra,
        int width,
        int height,
        int maxEdge = 896)
    {
        var rgb = FlattenBgraToRgb(bgra, width, height);
        var edge = Math.Max(width, height);
        if (edge <= maxEdge)
        {
            return (rgb, width, height);
        }

        var scale = maxEdge / (double)edge;
        var nw = Math.Max(1, (int)Math.Round(width * scale));
        var nh = Math.Max(1, (int)Math.Round(height * scale));
        return (Bilinear(rgb, width, height, nw, nh), nw, nh);
    }

    public static byte[] FlattenBgraToRgb(ReadOnlySpan<byte> bgra, int width, int height)
    {
        var rgb = new byte[width * height * 3];
        var n = width * height;
        for (var i = 0; i < n; i++)
        {
            var src = i * 4;
            var dst = i * 3;
            var a = bgra[src + 3];
            var inv = 255 - a;
            var b = (bgra[src] * a + 255 * inv) / 255;
            var g = (bgra[src + 1] * a + 255 * inv) / 255;
            var r = (bgra[src + 2] * a + 255 * inv) / 255;
            rgb[dst] = (byte)r;
            rgb[dst + 1] = (byte)g;
            rgb[dst + 2] = (byte)b;
        }

        return rgb;
    }

    public static byte[] PadToSquare(ReadOnlySpan<byte> rgb, int width, int height, byte fill, out int size)
    {
        size = Math.Max(width, height);
        var dest = new byte[size * size * 3];
        if (fill != 0)
        {
            dest.AsSpan().Fill(fill);
        }

        var ox = (size - width) / 2;
        var oy = (size - height) / 2;
        for (var y = 0; y < height; y++)
        {
            var srcRow = rgb.Slice(y * width * 3, width * 3);
            srcRow.CopyTo(dest.AsSpan(((oy + y) * size + ox) * 3, width * 3));
        }

        return dest;
    }

    public static byte[] Bicubic(ReadOnlySpan<byte> rgb, int sw, int sh, int dw, int dh)
    {
        var dest = new byte[dw * dh * 3];
        if (sw == dw && sh == dh)
        {
            rgb.CopyTo(dest);
            return dest;
        }

        var xRatio = sw / (double)dw;
        var yRatio = sh / (double)dh;
        for (var y = 0; y < dh; y++)
        {
            var sy = (y + 0.5) * yRatio - 0.5;
            var y0 = (int)Math.Floor(sy);
            var fy = (float)(sy - y0);
            for (var x = 0; x < dw; x++)
            {
                var sx = (x + 0.5) * xRatio - 0.5;
                var x0 = (int)Math.Floor(sx);
                var fx = (float)(sx - x0);
                for (var c = 0; c < 3; c++)
                {
                    var col = new float[4];
                    for (var ky = 0; ky < 4; ky++)
                    {
                        var row = new float[4];
                        var iy = y0 + ky - 1;
                        for (var kx = 0; kx < 4; kx++)
                        {
                            row[kx] = Sample(rgb, sw, sh, x0 + kx - 1, iy, c);
                        }

                        col[ky] = Cubic(row[0], row[1], row[2], row[3], fx);
                    }

                    dest[(y * dw + x) * 3 + c] = ClampToByte(Cubic(col[0], col[1], col[2], col[3], fy));
                }
            }
        }

        return dest;
    }

    public static byte[] Bilinear(ReadOnlySpan<byte> rgb, int sw, int sh, int dw, int dh)
    {
        var dest = new byte[dw * dh * 3];
        if (sw == dw && sh == dh)
        {
            rgb.CopyTo(dest);
            return dest;
        }

        var xRatio = sw / (double)dw;
        var yRatio = sh / (double)dh;
        for (var y = 0; y < dh; y++)
        {
            var sy = (y + 0.5) * yRatio - 0.5;
            var y0 = (int)Math.Floor(sy);
            var y1 = y0 + 1;
            var fy = (float)(sy - y0);
            for (var x = 0; x < dw; x++)
            {
                var sx = (x + 0.5) * xRatio - 0.5;
                var x0 = (int)Math.Floor(sx);
                var x1 = x0 + 1;
                var fx = (float)(sx - x0);
                for (var c = 0; c < 3; c++)
                {
                    var v00 = Sample(rgb, sw, sh, x0, y0, c);
                    var v10 = Sample(rgb, sw, sh, x1, y0, c);
                    var v01 = Sample(rgb, sw, sh, x0, y1, c);
                    var v11 = Sample(rgb, sw, sh, x1, y1, c);
                    var v0 = v00 + (v10 - v00) * fx;
                    var v1 = v01 + (v11 - v01) * fx;
                    dest[(y * dw + x) * 3 + c] = ClampToByte(v0 + (v1 - v0) * fy);
                }
            }
        }

        return dest;
    }

    private static float Sample(ReadOnlySpan<byte> rgb, int w, int h, int x, int y, int c)
    {
        x = Math.Clamp(x, 0, w - 1);
        y = Math.Clamp(y, 0, h - 1);
        return rgb[(y * w + x) * 3 + c];
    }

    private static float Cubic(float a, float b, float c, float d, float t)
    {
        var a0 = -0.5f * a + 1.5f * b - 1.5f * c + 0.5f * d;
        var a1 = a - 2.5f * b + 2f * c - 0.5f * d;
        var a2 = -0.5f * a + 0.5f * c;
        return ((a0 * t + a1) * t + a2) * t + b;
    }

    private static byte ClampToByte(float value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);
}
