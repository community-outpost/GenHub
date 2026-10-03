using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using GenHub.Features.Tools.ViewModels;

namespace GenHub.Features.Tools.Views.PublisherStudio;

/// <summary>
/// View for hosting provider setup, cloud storage metrics, and hosted asset inventory.
/// </summary>
public partial class HostingStorageView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HostingStorageView"/> class.
    /// </summary>
    public HostingStorageView()
    {
        InitializeComponent();
    }

    private static bool IsInsideButton(Visual? visual)
    {
        var current = visual;
        while (current != null)
        {
            if (current is Button || current is ToggleButton)
            {
                return true;
            }

            current = current.GetVisualParent();
        }

        return false;
    }

    private static bool IsInsideExpandedPanel(Visual? visual)
    {
        var current = visual;
        while (current != null)
        {
            if (current is Border border && border.Classes.Contains("expanded-panel"))
            {
                return true;
            }

            current = current.GetVisualParent();
        }

        return false;
    }

    private void OnAssetRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border row || row.DataContext is not HostedAssetItemViewModel item)
        {
            return;
        }

        if (e.Source is Visual source && (IsInsideButton(source) || IsInsideExpandedPanel(source)))
        {
            return;
        }

        if (item.IsExpandable && e.GetCurrentPoint(row).Properties.IsLeftButtonPressed)
        {
            item.IsExpanded = !item.IsExpanded;
        }
    }
}
