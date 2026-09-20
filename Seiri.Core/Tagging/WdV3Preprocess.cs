namespace Seiri.Core.Tagging;

public static class WdV3Preprocess
{
    public const int Size = 448;

    public static float[] FromBgra(ReadOnlySpan<byte> bgra, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Image size must be positive.");
        }

        var limited = ImageResize.FlattenAndLimit(bgra, width, height);
        var padded = ImageResize.PadToSquare(limited.Rgb, limited.Width, limited.Height, 255, out var square);
        var resized = ImageResize.Bicubic(padded, square, square, Size, Size);
        var tensor = new float[Size * Size * 3];
        for (var i = 0; i < Size * Size; i++)
        {
            var src = i * 3;
            var dst = i * 3;
            tensor[dst] = resized[src + 2];
            tensor[dst + 1] = resized[src + 1];
            tensor[dst + 2] = resized[src];
        }

        return tensor;
    }

    public static long[] Shape => [1, Size, Size, 3];
}
