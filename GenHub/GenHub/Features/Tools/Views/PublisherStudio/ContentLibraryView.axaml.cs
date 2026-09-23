using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using GenHub.Features.Tools.ViewModels;
using System;
using System.Diagnostics;
using System.Linq;

namespace GenHub.Features.Tools.Views.PublisherStudio;

/// <summary>
/// View for managing content library items.
/// </summary>
public partial class ContentLibraryView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ContentLibraryView"/> class.
    /// </summary>
    public ContentLibraryView()
    {
        InitializeComponent();
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver, handledEventsToo: true);
        AddHandler(DragDrop.DropEvent, OnDrop, handledEventsToo: true);
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains(DataFormats.Files))
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
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

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.Handled || !e.Data.Contains(DataFormats.Files))
        {
            return;
        }

        if (DataContext is not ContentLibraryViewModel vm)
        {
            return;
        }

        try
        {
            var files = e.Data.GetFiles();
            if (files == null)
            {
                return;
            }

            var paths = files
                .Select(f => f.Path?.LocalPath)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Cast<string>()
                .ToList();

            if (paths.Count == 0)
            {
                return;
            }

            var sourceVisual = e.Source as Visual;

            // 1. Dropped on Addons section or dropzone
            if (IsInSubtree(sourceVisual, "AddonsDropZone") || IsInSubtree(sourceVisual, "AddonsSection"))
            {
                e.Handled = true;
                await vm.AddAddonWithPathsAsync(paths);
                return;
            }

            // 2. Dropped on Releases section or dropzone
            if (IsInSubtree(sourceVisual, "ReleasesDropZone") || IsInSubtree(sourceVisual, "ReleasesSection"))
            {
                e.Handled = true;
                await vm.AddReleaseWithPathsAsync(paths);
                return;
            }

            // 3. Dropped on Left Catalog Panel or Content Items DropZone
            if (IsInSubtree(sourceVisual, "ContentItemsDropZone") || IsInSubtree(sourceVisual, "CatalogListPanel"))
            {
                e.Handled = true;
                await vm.AddContentWithPathsAsync(paths);
                return;
            }

            // 4. Dropped on Content Detail Panel when a content item is selected -> default to adding release
            if (vm.SelectedContent != null && IsInSubtree(sourceVisual, "ContentDetailPanel"))
            {
                e.Handled = true;
                await vm.AddReleaseWithPathsAsync(paths);
                return;
            }

            // 5. Default fallback: add new content item
            e.Handled = true;
            await vm.AddContentWithPathsAsync(paths);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to process dropped files: {ex}");
        }
    }
}
