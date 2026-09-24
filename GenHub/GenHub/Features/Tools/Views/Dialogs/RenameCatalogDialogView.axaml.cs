using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using GenHub.Features.Tools.ViewModels.Dialogs;
using System;
using System.Diagnostics;
using System.Linq;

namespace GenHub.Features.Tools.Views.Dialogs;

/// <summary>
/// Code-behind for RenameCatalogDialogView.
/// </summary>
public partial class RenameCatalogDialogView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RenameCatalogDialogView"/> class.
    /// </summary>
    public RenameCatalogDialogView()
    {
        InitializeComponent();
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver, handledEventsToo: true);
        AddHandler(DragDrop.DropEvent, OnDrop, handledEventsToo: true);
    }

    private static bool IsInSubtree(Visual? visual, string name)
    {
        while (visual != null)
        {
            if (visual is Control control && control.Name == name)
            {
                return true;
            }

            visual = visual.GetVisualParent();
        }

        return false;
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        var visual = e.Source as Visual;
        if ((IsInSubtree(visual, "IconDropTarget") || IsInSubtree(visual, "IconTextBox")) &&
            (e.Data.Contains(DataFormats.Files) || e.Data.Contains(DataFormats.Text)))
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        try
        {
            if (DataContext is not RenameCatalogDialogViewModel vm)
            {
                return;
            }

            var sourceVisual = e.Source as Visual;
            if (!IsInSubtree(sourceVisual, "IconDropTarget") && !IsInSubtree(sourceVisual, "IconTextBox"))
            {
                return;
            }

            if (e.Data.Contains(DataFormats.Files))
            {
                var files = e.Data.GetFiles()?.Select(f => f.Path?.LocalPath).Where(p => !string.IsNullOrEmpty(p)).ToList();
                if (files != null && files.Count > 0 && files[0] is { } filePath)
                {
                    e.Handled = true;
                    await vm.HandleIconDropAsync(filePath);
                    return;
                }
            }

            if (e.Data.Contains(DataFormats.Text))
            {
                var text = e.Data.GetText();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    e.Handled = true;
                    await vm.HandleIconDropAsync(text.Trim());
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"RenameCatalogDialogView: drop handling failed: {ex.Message}");
        }
    }
}
