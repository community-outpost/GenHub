using Avalonia.Controls;
using Avalonia.Input;
using GenHub.Features.Tools.ViewModels.Dialogs;
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
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver, handledEventsToo: true);
        AddHandler(DragDrop.DropEvent, OnDrop, handledEventsToo: true);

        var artifactsZone = this.FindControl<Border>("ArtifactsDropZone");
        if (artifactsZone != null)
        {
            artifactsZone.AddHandler(DragDrop.DragOverEvent, OnDragOver, handledEventsToo: true);
            artifactsZone.AddHandler(DragDrop.DropEvent, OnArtifactsDrop, handledEventsToo: true);
        }

        var imagesZone = this.FindControl<Border>("ImagesDropZone");
        if (imagesZone != null)
        {
            imagesZone.AddHandler(DragDrop.DragOverEvent, OnDragOver, handledEventsToo: true);
            imagesZone.AddHandler(DragDrop.DropEvent, OnImagesDrop, handledEventsToo: true);
        }
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

    private async void OnArtifactsDrop(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains(DataFormats.Files) || DataContext is not AddReleaseDialogViewModel vm)
        {
            return;
        }

        var files = e.Data.GetFiles();
        if (files == null) return;

        var paths = files
            .Select(f => f.Path?.LocalPath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Cast<string>()
            .ToList();

        if (paths.Count > 0)
        {
            e.Handled = true;
            await vm.AddArtifactsFromPathsAsync(paths);
        }
    }

    private async void OnImagesDrop(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains(DataFormats.Files) || DataContext is not AddReleaseDialogViewModel vm)
        {
            return;
        }

        var files = e.Data.GetFiles();
        if (files == null) return;

        var paths = files
            .Select(f => f.Path?.LocalPath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Cast<string>()
            .ToList();

        if (paths.Count > 0)
        {
            e.Handled = true;
            await vm.AddImagesFromPathsAsync(paths);
        }
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.Handled || !e.Data.Contains(DataFormats.Files) || DataContext is not AddReleaseDialogViewModel vm)
        {
            return;
        }

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

        if (paths.Count > 0)
        {
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
    }
}
