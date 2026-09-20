using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Seiri.Core.Models;

namespace Seiri.Views;

public sealed class GalleryTemplateSelector : DataTemplateSelector
{
    public DataTemplate? HeaderTemplate { get; set; }
    public DataTemplate? TileTemplate { get; set; }

    protected override DataTemplate SelectTemplateCore(object item) =>
        Pick(item);

    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container) =>
        Pick(item);

    private DataTemplate Pick(object item)
    {
        if (item is GalleryEntry { IsHeader: true })
        {
            return HeaderTemplate ?? TileTemplate!;
        }

        return TileTemplate ?? HeaderTemplate!;
    }
}
