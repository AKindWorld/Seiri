namespace Seiri.Core.Tagging;

public static class PixaiPreprocess
{
    public const int Size = 448;

    public static float[] FromBgra(ReadOnlySpan<byte> bgra, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Image size must be positive.");
        }

        var limited = ImageResize.FlattenAndLimit(bgra, width, height);
        var resized = ImageResize.Bilinear(limited.Rgb, limited.Width, limited.Height, Size, Size);
        var tensor = new float[3 * Size * Size];
        var plane = Size * Size;
        for (var i = 0; i < plane; i++)
        {
            var src = i * 3;
            tensor[i] = (resized[src] / 255f - 0.5f) / 0.5f;
            tensor[plane + i] = (resized[src + 1] / 255f - 0.5f) / 0.5f;
            tensor[plane * 2 + i] = (resized[src + 2] / 255f - 0.5f) / 0.5f;
        }

        return tensor;
    }

    public static long[] Shape => [1, 3, Size, Size];
}
