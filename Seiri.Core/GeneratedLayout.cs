namespace Seiri.Core;

public static class GeneratedLayout
{
    public const string FolderName = "thumbnail";
    public const string FallbackFolderName = "seiri-thumbnail";
    public const string MarkerFileName = ".seiri";
    public const string DatabaseFileName = "seiri.db";

    public static string GetFolder(string libraryRoot, string folderName = FolderName) =>
        Path.Combine(libraryRoot, folderName);

    public static string GetDatabasePath(string libraryRoot, string folderName = FolderName) =>
        Path.Combine(GetFolder(libraryRoot, folderName), DatabaseFileName);

    public static string GetMarkerPath(string libraryRoot, string folderName = FolderName) =>
        Path.Combine(GetFolder(libraryRoot, folderName), MarkerFileName);

    public static bool IsGeneratedDirectoryName(string name) =>
        name.Equals(FolderName, StringComparison.OrdinalIgnoreCase)
        || name.Equals(FallbackFolderName, StringComparison.OrdinalIgnoreCase)
        || name.Equals("@eaDir", StringComparison.OrdinalIgnoreCase)
        || name.Equals("__MACOSX", StringComparison.OrdinalIgnoreCase)
        || name.Equals(".git", StringComparison.OrdinalIgnoreCase);

    public static string ToRelativeUnix(string libraryRoot, string fullPath)
    {
        var rel = Path.GetRelativePath(libraryRoot, fullPath);
        return rel.Replace('\\', '/');
    }

    public static string ToFullPath(string libraryRoot, string relativeUnix) =>
        Path.GetFullPath(Path.Combine(libraryRoot, relativeUnix.Replace('/', Path.DirectorySeparatorChar)));

    public static string ThumbRelativeUnix(string mediaRelUnix) =>
        $"{FolderName}/{mediaRelUnix}.jpg";
}
