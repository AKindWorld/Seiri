using Seiri.Core;

namespace Seiri.Tests;

internal static class Fixture
{
    // 1x1 PNG
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    public static string CreateLibrary()
    {
        var root = Path.Combine(Path.GetTempPath(), "seiri-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "characters"));
        File.WriteAllBytes(Path.Combine(root, "hutao.png"), Png);
        File.WriteAllText(Path.Combine(root, "hutao.txt"), "1girl, long hair, hat");
        File.WriteAllBytes(Path.Combine(root, "characters", "zhongli.png"), Png);
        File.WriteAllBytes(Path.Combine(root, "untagged.png"), Png);
        File.WriteAllText(Path.Combine(root, "ignoreme.txt"), "orphan sidecar-looking file");
        return root;
    }
}
