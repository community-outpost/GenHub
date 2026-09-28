using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using GenHub.Common.Controls;
using GenHub.Features.Tools.ViewModels.Dialogs;
using System;
using System.Diagnostics;
using System.Linq;

namespace GenHub.Features.Tools.Views.Dialogs;

/// <summary>
/// View for adding or editing a release or addon.
/// Dropped files and folders are added as release artifacts or images with duplicate checking.
/// </summary>
public partial class AddReleaseDialogView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AddReleaseDialogView"/> class.
    /// </summary>
    public AddReleaseDialogView()
    {
        InitializeComponent();
        ViewDropHelper.EnableFileDrop(this, OnDrop);
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.Handled || !e.Data.Contains(DataFormats.Files) || DataContext is not AddReleaseDialogViewModel vm)
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

            var sourceVisual = e.Source as Visual;

            // Specific drop on ImagesZone
            if (ViewDropHelper.IsInSubtree(sourceVisual, "ImagesDropZone"))
            {
                e.Handled = true;
                await vm.AddImagesFromPathsAsync(paths);
                return;
            }

            // Specific drop on ArtifactsZone
            if (ViewDropHelper.IsInSubtree(sourceVisual, "ArtifactsDropZone"))
            {
                e.Handled = true;
                await vm.AddArtifactsFromPathsAsync(paths);
                return;
            }

            e.Handled = true;
            var allImages = paths.All(AddReleaseDialogViewModel.IsImageFile);
            if (allImages)
            {
                await vm.AddImagesFromPathsAsync(paths);
            }
            else
            {
                await vm.AddArtifactsFromPathsAsync(paths);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to process dropped files: {ex}");
            if (DataContext is AddReleaseDialogViewModel dialogVm)
            {
                dialogVm.NotifyDropFailed();
            }
        }
    }
}
