namespace Seiri.Core.Models;

public sealed class AppSettings
{
    public string Theme { get; set; } = "System";
    public GalleryLayoutMode Layout { get; set; } = GalleryLayoutMode.Grid;
    public GroupKey GroupBy { get; set; } = GroupKey.DateTaken;
    public LayoutDensity LayoutDensity { get; set; } = LayoutDensity.Medium;
    public bool BadgeVisible { get; set; } = true;
    public string BadgePosition { get; set; } = "TopRight";
    public bool HideBadgeWhenZero { get; set; } = true;
    public bool PreviewPaneOpen { get; set; } = true;
    public bool RailExpanded { get; set; }
    public int MaxTags { get; set; } = 128;
    public List<string> EnabledModelIds { get; set; } = [];
    public string ExecutionProvider { get; set; } = "Auto";
    public int TagBatchSize { get; set; } = 1;
    public string MergeStrategy { get; set; } = "UnionMax";
    public Dictionary<string, ThresholdPreset> ModelPresets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string SidecarFormat { get; set; } = "comma-space";
    public bool WriteUnderscores { get; set; }
    public bool CreateSidecarIfMissing { get; set; } = true;
    public int WindowWidth { get; set; } = 1280;
    public int WindowHeight { get; set; } = 800;
    public int WindowX { get; set; } = int.MinValue;
    public int WindowY { get; set; } = int.MinValue;
    public bool WindowMaximized { get; set; }
    public bool WindowFullScreen { get; set; }
    public bool HasSeenSearchTip { get; set; }
    /// <summary>
    /// Null means unset (treat as on). WinUI already composites on the GPU;
    /// false forces CPU software bitmaps for tiles.
    /// </summary>
    public bool? HardwareAcceleration { get; set; }
    public List<ModelCatalogEntry> CustomModels { get; set; } = [];
    public bool UseDanbooruAliases { get; set; }
    public string? SimilarModelId { get; set; }
    /// <summary>
    /// When false (default), typing only updates suggestions; Enter or the search icon runs the query.
    /// </summary>
    public bool SearchAsYouType { get; set; }
}

public sealed class LibrariesFile
{
    public List<string> Roots { get; set; } = [];
}
