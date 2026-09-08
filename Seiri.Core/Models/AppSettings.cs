namespace Seiri.Core.Models;

public sealed class AppSettings
{
    public string Theme { get; set; } = "System";
    public GalleryLayoutMode Layout { get; set; } = GalleryLayoutMode.Grid;
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
    public bool HasSeenSearchTip { get; set; }
}

public sealed class LibrariesFile
{
    public List<string> Roots { get; set; } = [];
}
