using GenHub.Core.Constants;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace GenHub.Common.ViewModels;

/// <summary>
/// An observable collection whose contents can be replaced with a single change notification.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
public class BatchObservableCollection<T> : ObservableCollection<T>
{
    /// <summary>
    /// Replaces every item and raises one reset notification.
    /// </summary>
    /// <param name="items">The new items.</param>
    public void ReplaceAll(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        CheckReentrancy();
        var replacements = items.ToList();
        Items.Clear();
        foreach (var item in replacements)
        {
            Items.Add(item);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs(UiConstants.IndexerPropertyName));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
