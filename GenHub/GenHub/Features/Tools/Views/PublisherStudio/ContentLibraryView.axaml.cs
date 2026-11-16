using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using GenHub.Common.Controls;
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
        ViewDropHelper.EnableFileDrop(this, OnDrop);
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
        if (ViewDropHelper.IsInSubtree(sourceVisual, "AddonsDropZone") || ViewDropHelper.IsInSubtree(sourceVisual, "AddonsSection"))
        {
            await vm.AddAddonWithPathsAsync(paths);
            return;
        }

        if (ViewDropHelper.IsInSubtree(sourceVisual, "ReleasesDropZone") || ViewDropHelper.IsInSubtree(sourceVisual, "ReleasesSection"))
        {
            await vm.AddReleaseWithPathsAsync(paths);
            return;
        }

        if (ViewDropHelper.IsInSubtree(sourceVisual, "ContentItemsDropZone") || ViewDropHelper.IsInSubtree(sourceVisual, "CatalogListPanel"))
        {
            await AddContentItemsDropAsync(vm, paths);
            return;
        }

        if (ViewDropHelper.IsInSubtree(sourceVisual, "MediaScreenshotsDropZone") || ViewDropHelper.IsInSubtree(sourceVisual, "MediaVideosDropZone"))
        {
            await vm.AddMediaToSelectedContentAsync(paths);
            return;
        }

        if (vm.SelectedContent != null && ViewDropHelper.IsInSubtree(sourceVisual, "ContentDetailPanel"))
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
            var paths = ViewDropHelper.ExtractDroppedPaths(e);
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
