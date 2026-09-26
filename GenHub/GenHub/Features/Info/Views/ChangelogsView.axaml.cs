using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GenHub.Features.Info.ViewModels;
using System;
using System.Collections.Generic;
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
    public Control? ContainerFromItem(object item)
    {
        var container = ReleasesItemsControl?.ContainerFromItem(item);
        if (container != null)
        {
            return container;
        }

        if (ReleasesItemsControl?.ItemsSource is IEnumerable<ChangelogItemViewModel> releases)
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
                var indexedContainer = ReleasesItemsControl.ContainerFromIndex(index);
                if (indexedContainer != null)
                {
                    return indexedContainer;
                }
            }
        }

        return null;
    }
}
