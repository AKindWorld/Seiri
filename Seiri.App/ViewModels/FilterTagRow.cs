using CommunityToolkit.Mvvm.ComponentModel;

namespace Seiri.ViewModels;

public sealed partial class FilterTagRow : ObservableObject
{
    public required string Name { get; init; }
    public string Category { get; init; } = "general";
    public int UseCount { get; init; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public string CountText => UseCount.ToString("N0");
}

public sealed partial class FilterChoice : ObservableObject
{
    public required string Label { get; init; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
