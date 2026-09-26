using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using GenHub.Core.Helpers;
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

    private static async Task<bool> TryHandleDropZonesAsync(Visual? sourceVisual, List<string> paths, AddContentDialogViewModel vm)
    {
        if (IsInSubtree(sourceVisual, "ContentMediaDropZone"))
        {
            await vm.AddScreenshotsFromPathsAsync(paths);
            return true;
        }

        if (IsInSubtree(sourceVisual, "VideoDropTarget"))
        {
            var videoPaths = paths.Where(IsVideoFile).ToList();
            if (videoPaths.Count > 0)
            {
                await vm.AddVideosFromPathsAsync(videoPaths);
                return true;
            }

            return false;
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
        var videos = paths.Where(IsVideoFile).ToList();
        var images = paths.Where(IsImageFile).ToList();

        if (videos.Count == paths.Count)
        {
            await vm.AddVideosFromPathsAsync(videos);
            return;
        }

        if (images.Count == paths.Count)
        {
            await RouteImagesAsync(images, vm);
            return;
        }

        if (videos.Count > 0 || images.Count > 0)
        {
            if (videos.Count > 0)
            {
                await vm.AddVideosFromPathsAsync(videos);
            }

            if (images.Count > 0)
            {
                await RouteImagesAsync(images, vm);
            }

            var others = paths.Where(p => !IsVideoFile(p) && !IsImageFile(p)).ToList();
            if (others.Count > 0)
            {
                vm.PopulateFromPaths(others);
            }

            return;
        }

        vm.PopulateFromPaths(paths);
    }

    private static async Task RouteImagesAsync(List<string> images, AddContentDialogViewModel vm)
    {
        if (images.Count == 0) return;

        if (string.IsNullOrWhiteSpace(vm.IconArtwork))
        {
            vm.IconArtwork = images[0];
            if (images.Count > 1)
            {
                await vm.AddScreenshotsFromPathsAsync(images.Skip(1));
            }
        }
        else if (string.IsNullOrWhiteSpace(vm.BannerArtwork))
        {
            vm.BannerArtwork = images[0];
            if (images.Count > 1)
            {
                await vm.AddScreenshotsFromPathsAsync(images.Skip(1));
            }
        }
        else
        {
            await vm.AddScreenshotsFromPathsAsync(images);
        }
    }

    private static bool IsImageFile(string path) => MediaFileHelper.IsImageFile(path);

    private static bool IsVideoFile(string path) => MediaFileHelper.IsVideoFile(path);

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
