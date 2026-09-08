using Seiri.Infrastructure;

namespace Seiri.Tests;

public class ImageDimensionsTests
{
    [Fact]
    public void Reads_png_size()
    {
        var path = Path.GetFullPath(Path.Combine("..", "..", "..", "..", "test-library", "hutao.png"));
        if (!File.Exists(path))
        {
            path = Path.GetFullPath(Path.Combine("..", "..", "..", "..", "..", "test-library", "hutao.png"));
        }

        Assert.True(File.Exists(path), path);
        var size = ImageDimensions.TryRead(path);
        Assert.NotNull(size);
        Assert.Equal(50, size.Value.Width);
        Assert.Equal(50, size.Value.Height);
    }

    [Fact]
    public void Reads_wide_png_size()
    {
        var path = Path.GetFullPath(Path.Combine("..", "..", "..", "..", "test-library", "characters", "zhongli.png"));
        if (!File.Exists(path))
        {
            path = Path.GetFullPath(Path.Combine("..", "..", "..", "..", "..", "test-library", "characters", "zhongli.png"));
        }

        Assert.True(File.Exists(path), path);
        var size = ImageDimensions.TryRead(path);
        Assert.NotNull(size);
        Assert.Equal(620, size.Value.Width);
        Assert.Equal(300, size.Value.Height);
    }
}
