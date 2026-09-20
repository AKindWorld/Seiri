namespace Seiri.Core.Tagging;

public static class ClipPreprocess
{
    public const int Size = 224;

    public static float[] FromBgra(ReadOnlySpan<byte> bgra, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Image size must be positive.");
        }

        var rgb = ImageResize.FlattenBgraToRgb(bgra, width, height);
        var resized = ImageResize.Bilinear(rgb, width, height, Size, Size);
        var tensor = new float[3 * Size * Size];
        var plane = Size * Size;
        for (var i = 0; i < plane; i++)
        {
            var src = i * 3;
            tensor[i] = (resized[src] / 255f - 0.48145466f) / 0.26862954f;
            tensor[plane + i] = (resized[src + 1] / 255f - 0.4578275f) / 0.26130258f;
            tensor[plane * 2 + i] = (resized[src + 2] / 255f - 0.40821073f) / 0.27577711f;
        }

        return tensor;
    }

    public static long[] Shape => [1, 3, Size, Size];
}
