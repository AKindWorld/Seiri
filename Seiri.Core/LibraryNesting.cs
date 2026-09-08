namespace Seiri.Core;

public static class LibraryNesting
{
    public static bool Conflicts(string candidate, IEnumerable<string> existingRoots)
    {
        var c = Normalize(candidate);
        foreach (var root in existingRoots)
        {
            var e = Normalize(root);
            if (c.Equals(e, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (IsNested(c, e) || IsNested(e, c))
            {
                return true;
            }
        }

        return false;
    }

    public static string Normalize(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool IsNested(string inner, string outer)
    {
        if (!inner.StartsWith(outer, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return inner.Length > outer.Length
            && (inner[outer.Length] == Path.DirectorySeparatorChar || inner[outer.Length] == Path.AltDirectorySeparatorChar);
    }
}
