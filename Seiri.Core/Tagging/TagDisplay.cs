namespace Seiri.Core.Tagging;

public static class TagDisplay
{
    private static readonly HashSet<string> Kaomoji = new(StringComparer.Ordinal)
    {
        "0_0", "(o)_(o)", "+_+", "+_-", "._.", "<o>_<o>", "<|>_<|>", "=_=", ">_<",
        "3_3", "6_9", ">_o", "@_@", "^_^", "o_o", "u_u", "x_x", "|_|", "||_||",
        "._0", "o_0", "o_o", "O_O", "0_o"
    };

    public static string ToCanonical(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return trimmed;
        }

        if (Kaomoji.Contains(trimmed))
        {
            return trimmed;
        }

        return trimmed.Replace('_', ' ').ToLowerInvariant();
    }

    public static bool IsKaomoji(string name) => Kaomoji.Contains(name);
}
