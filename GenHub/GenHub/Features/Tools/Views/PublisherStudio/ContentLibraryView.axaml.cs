using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using GenHub.Features.Tools.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

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

    private static List<string> ExtractDroppedPaths(DragEventArgs e)
    {
        return e.Data.GetFiles()?
            .Select(f => f.Path?.LocalPath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Cast<string>()
            .ToList() ?? [];
    }

    private static async Task<bool> TryImportCatalogDropAsync(ContentLibraryViewModel vm, List<string> paths, DragEventArgs e)
    {
        if (paths.Count != 1 || !Path.GetExtension(paths[0]).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        e.Handled = true;
        return await vm.TryImportCatalogFileAsync(paths[0], announceFailures: true);
    }

    private static async Task RouteDroppedPathsAsync(ContentLibraryViewModel vm, List<string> paths, Visual? sourceVisual)
    {
        if (IsInSubtree(sourceVisual, "AddonsDropZone") || IsInSubtree(sourceVisual, "AddonsSection"))
        {
            await vm.AddAddonWithPathsAsync(paths);
            return;
        }

        if (IsInSubtree(sourceVisual, "ReleasesDropZone") || IsInSubtree(sourceVisual, "ReleasesSection"))
        {
            await vm.AddReleaseWithPathsAsync(paths);
            return;
        }

        if (IsInSubtree(sourceVisual, "ContentItemsDropZone") || IsInSubtree(sourceVisual, "CatalogListPanel"))
        {
            await AddContentItemsDropAsync(vm, paths);
            return;
        }

        if (IsInSubtree(sourceVisual, "MediaScreenshotsDropZone") || IsInSubtree(sourceVisual, "MediaVideosDropZone"))
        {
            await vm.AddMediaToSelectedContentAsync(paths);
            return;
        }

        if (vm.SelectedContent != null && IsInSubtree(sourceVisual, "ContentDetailPanel"))
        {
            await vm.AddReleaseWithPathsAsync(paths);
            return;
        }

        await vm.AddContentWithPathsAsync(paths);
    }

    private static async Task AddContentItemsDropAsync(ContentLibraryViewModel vm, List<string> paths)
    {
        if (paths.Count > 1)
        {
            await vm.BatchImportContentItemsAsync(paths);
        }
        else
        {
            await vm.AddContentWithPathsAsync(paths);
        }
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
            var paths = ExtractDroppedPaths(e);
            if (paths.Count == 0)
            {
                return;
            }

            if (await TryImportCatalogDropAsync(vm, paths, e))
            {
                return;
            }

            e.Handled = true;
            await RouteDroppedPathsAsync(vm, paths, e.Source as Visual);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to process dropped files: {ex}");
            vm.NotifyDropFailed();
        }
    }
}
