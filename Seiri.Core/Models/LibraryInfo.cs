namespace Seiri.Core.Models;

public sealed class LibraryInfo
{
    public required string RootPath { get; init; }
    public string DisplayName => new DirectoryInfo(RootPath).Name;
    public string GeneratedFolderName { get; init; } = GeneratedLayout.FolderName;
    public bool Recursive { get; init; } = true;
    public DateTimeOffset? LastScanAt { get; init; }
    public int FileCount { get; init; }
    public bool IsOffline { get; init; }
}
