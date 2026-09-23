using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using GenHub.Features.Tools.ViewModels.Dialogs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.Views.Dialogs;

/// <summary>
/// View for adding new content.
/// </summary>
public partial class AddContentDialogView : UserControl
{
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".ico"];

    /// <summary>
    /// Initializes a new instance of the <see cref="AddContentDialogView"/> class.
    /// </summary>
    public AddContentDialogView()
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

    private static bool TryHandleArtworkDrop(Visual? sourceVisual, List<string> paths, AddContentDialogViewModel vm)
    {
        var firstPath = paths[0];
        if (!IsImageFile(firstPath))
        {
            return false;
        }

        if (IsInSubtree(sourceVisual, "IconDropTarget") || IsInSubtree(sourceVisual, "IconTextBox"))
        {
            vm.IconArtwork = firstPath;
            return true;
        }

        if (IsInSubtree(sourceVisual, "BannerDropTarget") || IsInSubtree(sourceVisual, "BannerTextBox"))
        {
            vm.BannerArtwork = firstPath;
            return true;
        }

        if (IsInSubtree(sourceVisual, "BackdropDropTarget") || IsInSubtree(sourceVisual, "BackdropTextBox"))
        {
            vm.BackdropArtwork = firstPath;
            return true;
        }

        return false;
    }

    private static async Task<bool> TryHandleDropZonesAsync(Visual? sourceVisual, List<string> paths, AddContentDialogViewModel vm)
    {
        if (IsInSubtree(sourceVisual, "ContentMediaDropZone"))
        {
            await vm.AddScreenshotsFromPathsAsync(paths);
            return true;
        }

        if (IsInSubtree(sourceVisual, "InitialReleaseDropZone") || IsInSubtree(sourceVisual, "InitialReleaseSection"))
        {
            await vm.AddReleaseArtifactsFromPathsAsync(paths);
            return true;
        }

        return false;
    }

    private static async Task HandleFallbackDropAsync(List<string> paths, AddContentDialogViewModel vm)
    {
        var allImages = paths.All(IsImageFile);
        if (!allImages)
        {
            vm.PopulateFromPaths(paths);
            return;
        }

        if (string.IsNullOrWhiteSpace(vm.IconArtwork))
        {
            vm.IconArtwork = paths[0];
            if (paths.Count > 1)
            {
                await vm.AddScreenshotsFromPathsAsync(paths.Skip(1));
            }
        }
        else if (string.IsNullOrWhiteSpace(vm.BannerArtwork))
        {
            vm.BannerArtwork = paths[0];
            if (paths.Count > 1)
            {
                await vm.AddScreenshotsFromPathsAsync(paths.Skip(1));
            }
        }
        else
        {
            await vm.AddScreenshotsFromPathsAsync(paths);
        }
    }

    private static bool IsImageFile(string path) =>
        ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

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
        if (e.Handled || !e.Data.Contains(DataFormats.Files) || DataContext is not AddContentDialogViewModel vm)
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

            if (TryHandleArtworkDrop(sourceVisual, paths, vm))
            {
                e.Handled = true;
                return;
            }

            if (await TryHandleDropZonesAsync(sourceVisual, paths, vm))
            {
                e.Handled = true;
                return;
            }

            e.Handled = true;
            await HandleFallbackDropAsync(paths, vm);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to process dropped files: {ex}");
            if (DataContext is AddContentDialogViewModel dialogVm)
            {
                dialogVm.NotifyDropFailed();
            }
        }
    }
}
