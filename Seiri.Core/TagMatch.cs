namespace Seiri.Core;

public static class TagMatch
{
    public static bool Contains(string name, string needle)
    {
        if (string.IsNullOrEmpty(needle))
        {
            return true;
        }

        if (name.Contains(needle, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var parts = needle.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length <= 1)
        {
            return false;
        }

        return parts.All(part => name.Contains(part, StringComparison.OrdinalIgnoreCase));
    }

    public static int Rank(string name, string needle)
    {
        if (name.Equals(needle, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (name.StartsWith(needle, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        var idx = name.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        if (idx > 0 && name[idx - 1] == ' ')
        {
            return 2;
        }

        return 3;
    }
}
