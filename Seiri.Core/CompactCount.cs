namespace Seiri.Core.Models;

public static class CompactCount
{
    public static string Format(int value) =>
        value >= 1_000_000 ? $"{value / 1_000_000d:0.#}M"
        : value >= 1_000 ? $"{value / 1_000d:0.#}k"
        : value.ToString();
}
