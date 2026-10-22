using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GenHub.Features.Info.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace GenHub.Features.Info.Views;

/// <summary>
/// Interaction logic for ChangelogsView.axaml.
/// </summary>
public partial class ChangelogsView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ChangelogsView"/> class.
    /// </summary>
    public ChangelogsView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Gets the container control for a specific changelog item.
    /// </summary>
    /// <param name="item">The changelog item.</param>
    /// <returns>The container control if found; otherwise, null.</returns>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Accesses view instance controls")]
    public Control? ContainerFromItem(object item)
    {
        var itemsControl = this.FindControl<ItemsControl>("ReleasesItemsControl") ?? ReleasesItemsControl;
        var container = itemsControl?.ContainerFromItem(item);
        if (container != null)
        {
            return container;
        }

        if (itemsControl?.ItemsSource is IEnumerable<ChangelogItemViewModel> releases)
        {
            var releaseList = releases.ToList();
            int index = -1;

            if (item is ChangelogItemViewModel chItem)
            {
                index = releaseList.FindIndex(r =>
                    (!string.IsNullOrEmpty(r.Release.TagName) && string.Equals(r.Release.TagName, chItem.Release.TagName, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(r.Release.Name) && string.Equals(r.Release.Name, chItem.Release.Name, StringComparison.OrdinalIgnoreCase)));
                if (index < 0)
                {
                    index = releaseList.IndexOf(chItem);
                }
            }

            if (index >= 0)
            {
                var indexedContainer = itemsControl.ContainerFromIndex(index);
                if (indexedContainer != null)
                {
                    return indexedContainer;
                }
            }
        }

        return null;
    }
}
