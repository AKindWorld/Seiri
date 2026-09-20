using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Seiri.Core;
using Seiri.ViewModels;
using Windows.System;

namespace Seiri.Views;

public sealed partial class SearchBox : UserControl
{
    private bool _applying;
    private int _lostFocusGeneration;

    public GalleryViewModel Gallery => App.Shell.Gallery;

    public static Visibility BoolVis(bool value) =>
        value ? Visibility.Visible : Visibility.Collapsed;

    private async void OnClearSearch(object sender, RoutedEventArgs e) =>
        await Gallery.ClearSearchCommand.ExecuteAsync(null);

    public SearchBox()
    {
        InitializeComponent();
        QueryBox.Loaded += (_, _) => HideNativeClear();
    }

    private void HideNativeClear()
    {
        var inner = FindDescendant<TextBox>(QueryBox);
        if (inner is null)
        {
            return;
        }

        inner.Loaded += (_, _) => CollapseNamedButton(inner, "DeleteButton");
        CollapseNamedButton(inner, "DeleteButton");
    }

    private static void CollapseNamedButton(DependencyObject root, string name)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Button { Name: var n } button && n == name)
            {
                button.Visibility = Visibility.Collapsed;
                button.Width = 0;
                button.IsHitTestVisible = false;
                return;
            }

            CollapseNamedButton(child, name);
        }
    }

    public void FocusQuery()
    {
        _applying = true;
        _lostFocusGeneration++;
        try
        {
            QueryBox.Focus(FocusState.Programmatic);
            var inner = FindDescendant<TextBox>(QueryBox);
            if (inner is not null)
            {
                var text = inner.Text ?? string.Empty;
                inner.Select(text.Length, 0);
            }
        }
        finally
        {
            _applying = false;
        }
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            var nested = FindDescendant<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    public void CancelPendingDismiss() => _lostFocusGeneration++;

    private async void OnTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        sender.IsSuggestionListOpen = false;
        HideNativeClear();
        if (_applying || args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        try
        {
            await Gallery.HandleSearchInputAsync(sender.Text);
        }
        catch (Exception ex)
        {
            AppLog.Error("search text", ex);
        }
    }

    private async void OnGotFocus(object sender, RoutedEventArgs e)
    {
        QueryBox.IsSuggestionListOpen = false;
        if (_applying)
        {
            return;
        }

        await Gallery.UpdateSuggestionsAsync(QueryBox.Text);
        if (!App.Shell.Settings.HasSeenSearchTip && SearchTip is not null)
        {
            SearchTip.IsOpen = true;
        }
    }

    private void OnSearchTipClosed(TeachingTip sender, TeachingTipClosedEventArgs args)
    {
        App.Shell.Settings.HasSeenSearchTip = true;
        App.Shell.PersistSettings();
    }

    private void OnLostFocus(object sender, RoutedEventArgs e)
    {
        QueryBox.IsSuggestionListOpen = false;
    }

    private async void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        QueryBox.IsSuggestionListOpen = false;
        if (e.Key is VirtualKey.Escape)
        {
            if (Gallery.SuggestionsOpen)
            {
                Gallery.CloseSuggestions();
            }
            else if (!string.IsNullOrWhiteSpace(QueryBox.Text))
            {
                await Gallery.ClearSearchCommand.ExecuteAsync(null);
            }

            e.Handled = true;
        }
        else if (e.Key is VirtualKey.Back
                 && string.IsNullOrEmpty(QueryBox.Text)
                 && Gallery.SuggestionsOpen)
        {
            Gallery.CloseSuggestions();
            e.Handled = true;
        }
    }

    private async void OnQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        sender.IsSuggestionListOpen = false;
        if (_applying)
        {
            return;
        }

        _applying = true;
        _lostFocusGeneration++;
        try
        {
            Gallery.CloseSuggestions();
            await Gallery.ApplySearchAsync(sender.Text);
        }
        catch (Exception ex)
        {
            AppLog.Error("search submit", ex);
        }
        finally
        {
            _applying = false;
        }
    }
}
