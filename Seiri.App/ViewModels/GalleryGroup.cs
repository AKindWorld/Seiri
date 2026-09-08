using System.Collections.ObjectModel;
using Seiri.Core.Models;

namespace Seiri.ViewModels;

public sealed class GalleryGroup
{
    public required string Header { get; init; }
    public bool ShowHeader => Header.Length > 0;
    public ObservableCollection<MediaItem> Items { get; } = [];
}
