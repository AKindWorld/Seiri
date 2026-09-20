using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Seiri.Core;

/// <summary>
/// Observable list that can replace its contents with a single Reset event.
/// Per-item Add on 50k rows freezes the dispatcher; Reset does not.
/// </summary>
public sealed class ResettableCollection<T> : ObservableCollection<T>
{
    public int Version { get; private set; }

    public void ReplaceAll(IReadOnlyList<T> items)
    {
        CheckReentrancy();
        Items.Clear();
        for (var i = 0; i < items.Count; i++)
        {
            Items.Add(items[i]);
        }

        Version++;
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
