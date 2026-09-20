using System.Text.RegularExpressions;

namespace Seiri.Core;

public static class CustomModelSpec
{
    public const long MinOnnxBytes = 256 * 1024;
    public const long MaxOnnxBytes = 8L * 1024 * 1024 * 1024;
    public const int MinCsvTags = 1;

    private static readonly Regex RepoRx = new(@"^[A-Za-z0-9._-]+/[A-Za-z0-9._-]+$", RegexOptions.Compiled);

    public static bool TryParseRepo(string? raw, out string repo)
    {
        repo = (raw ?? string.Empty).Trim();
        if (repo.Length == 0)
        {
            return false;
        }

        if (repo.StartsWith("https://huggingface.co/", StringComparison.OrdinalIgnoreCase))
        {
            repo = repo["https://huggingface.co/".Length..];
        }
        else if (repo.StartsWith("http://huggingface.co/", StringComparison.OrdinalIgnoreCase))
        {
            repo = repo["http://huggingface.co/".Length..];
        }

        repo = repo.Trim('/');
        var slash = repo.IndexOf('/');
        if (slash < 0)
        {
            return false;
        }

        var org = repo[..slash];
        var rest = repo[(slash + 1)..];
        var name = rest.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (name.Length == 0)
        {
            return false;
        }

        repo = org + "/" + name[0];
        if (org is "." or ".." || name[0] is "." or ".."
            || org.Contains("..", StringComparison.Ordinal) || name[0].Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        return RepoRx.IsMatch(repo);
    }

    public static string IdFromRepo(string repo) =>
        "custom-" + repo.Replace('/', '-').ToLowerInvariant();

    public static string IdFromFile(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var slug = new string([.. name.ToLowerInvariant().Select(ch => char.IsAsciiLetterOrDigit(ch) ? ch : '-')]);
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        slug = slug.Trim('-');
        if (slug.Length == 0)
        {
            slug = "local";
        }

        return "custom-" + slug;
    }

    public static bool IsAllowedPreprocess(string? preprocess) =>
        preprocess is "WdV3" or "PixaiV09" or "Clip224";

    public static string SanitizePreprocess(string? preprocess) =>
        IsAllowedPreprocess(preprocess) ? preprocess! : "WdV3";
}
