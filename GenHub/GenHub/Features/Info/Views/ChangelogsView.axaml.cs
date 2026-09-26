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

        if (item is ChangelogItemViewModel chItem && ReleasesItemsControl?.ItemsSource is IEnumerable<ChangelogItemViewModel> releases)
        {
            var matched = releases.FirstOrDefault(r =>
                (!string.IsNullOrEmpty(r.Release.TagName) && string.Equals(r.Release.TagName, chItem.Release.TagName, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(r.Release.Name) && string.Equals(r.Release.Name, chItem.Release.Name, StringComparison.OrdinalIgnoreCase)));
            if (matched != null)
            {
                return ReleasesItemsControl.ContainerFromItem(matched);
            }
        }

        return null;
    }
}
