using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Seiri.Core;
using Seiri.Core.Models;
using Seiri.ViewModels;

namespace Seiri.Views;

public sealed partial class TagsPage : Page
{
    public GalleryViewModel Gallery => App.Shell.Gallery;

    public TagsPage()
    {
        InitializeComponent();
    }

    public static Visibility BoolVis(bool value) =>
        value ? Visibility.Visible : Visibility.Collapsed;

    private void OnTagNeedle(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        sender.IsSuggestionListOpen = false;
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput
            || args.Reason == AutoSuggestionBoxTextChangeReason.ProgrammaticChange)
        {
            Gallery.TagNeedle = sender.Text ?? string.Empty;
        }
    }

    private void OnInclude(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TagRecord tag })
        {
            Gallery.StageIncludeCommand.Execute(tag);
        }
    }

    private void OnExclude(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TagRecord tag })
        {
            Gallery.StageExcludeCommand.Execute(tag);
        }
    }

    private void OnSearch(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TagRecord tag })
        {
            AppLog.Run(() => Gallery.SearchRiverTagCommand.ExecuteAsync(tag), "search river tag");
        }
    }

    private void OnUnstageInclude(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TagRecord tag })
        {
            Gallery.UnstageIncludeCommand.Execute(tag);
        }
    }

    private void OnUnstageExclude(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TagRecord tag })
        {
            Gallery.UnstageExcludeCommand.Execute(tag);
        }
    }
}
