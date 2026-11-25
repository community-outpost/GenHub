using Avalonia.Controls;
using GenHub.Core.Models.Info;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace GenHub.Features.Info.Views;

/// <summary>
/// Interaction logic for GeneralsOnlineChangelogView.axaml.
/// </summary>
public partial class GeneralsOnlineChangelogView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GeneralsOnlineChangelogView"/> class.
    /// </summary>
    public GeneralsOnlineChangelogView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Gets the container control for a specific patch note item.
    /// </summary>
    /// <param name="item">The patch note item.</param>
    /// <returns>The container control if found; otherwise, null.</returns>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Accesses view instance controls")]
    public Control? ContainerFromItem(object item)
    {
        var itemsControl = this.FindControl<ItemsControl>("PatchNotesItemsControl") ?? PatchNotesItemsControl;
        var container = itemsControl?.ContainerFromItem(item);
        if (container != null)
        {
            return container;
        }

        if (itemsControl?.ItemsSource is IEnumerable<PatchNote> notes)
        {
            var noteList = notes.ToList();
            int index = -1;

            if (item is PatchNote pn)
            {
                index = noteList.FindIndex(n =>
                    (!string.IsNullOrEmpty(n.Id) && string.Equals(n.Id, pn.Id, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(n.Title) && string.Equals(n.Title, pn.Title, StringComparison.OrdinalIgnoreCase)));
                if (index < 0)
                {
                    index = noteList.IndexOf(pn);
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
