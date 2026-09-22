using Avalonia.Controls;
using Avalonia.Input;
using GenHub.Features.Tools.ViewModels.Dialogs;
using System.IO;
using System.Linq;

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

        var contentMediaDropZone = this.FindControl<Border>("ContentMediaDropZone");
        if (contentMediaDropZone != null)
        {
            contentMediaDropZone.AddHandler(DragDrop.DragOverEvent, OnDragOver, handledEventsToo: true);
            contentMediaDropZone.AddHandler(DragDrop.DropEvent, OnContentMediaDrop, handledEventsToo: true);
        }

        var initialReleaseDropZone = this.FindControl<Border>("InitialReleaseDropZone");
        if (initialReleaseDropZone != null)
        {
            initialReleaseDropZone.AddHandler(DragDrop.DragOverEvent, OnDragOver, handledEventsToo: true);
            initialReleaseDropZone.AddHandler(DragDrop.DropEvent, OnInitialReleaseDrop, handledEventsToo: true);
        }

        var releaseMediaDropZone = this.FindControl<Border>("ReleaseMediaDropZone");
        if (releaseMediaDropZone != null)
        {
            releaseMediaDropZone.AddHandler(DragDrop.DragOverEvent, OnDragOver, handledEventsToo: true);
            releaseMediaDropZone.AddHandler(DragDrop.DropEvent, OnReleaseMediaDrop, handledEventsToo: true);
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

    private async void OnContentMediaDrop(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains(DataFormats.Files)) return;
        if (DataContext is not AddContentDialogViewModel vm) return;

        var files = e.Data.GetFiles();
        if (files != null)
        {
            var paths = files
                .Select(f => f.Path?.LocalPath)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Cast<string>()
                .ToList();
            if (paths.Count > 0)
            {
                e.Handled = true;
                await vm.AddScreenshotsFromPathsAsync(paths);
            }
        }
    }

    private async void OnInitialReleaseDrop(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains(DataFormats.Files)) return;
        if (DataContext is not AddContentDialogViewModel vm) return;

        var files = e.Data.GetFiles();
        if (files != null)
        {
            var paths = files
                .Select(f => f.Path?.LocalPath)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Cast<string>()
                .ToList();
            if (paths.Count > 0)
            {
                e.Handled = true;
                await vm.AddReleaseArtifactsFromPathsAsync(paths);
            }
        }
    }

    private async void OnReleaseMediaDrop(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains(DataFormats.Files)) return;
        if (DataContext is not AddContentDialogViewModel vm) return;

        var files = e.Data.GetFiles();
        if (files != null)
        {
            var paths = files
                .Select(f => f.Path?.LocalPath)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Cast<string>()
                .ToList();
            if (paths.Count > 0)
            {
                e.Handled = true;
                await vm.AddReleaseImagesFromPathsAsync(paths);
            }
        }
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.Handled || !e.Data.Contains(DataFormats.Files))
        {
            return;
        }

        if (DataContext is not AddContentDialogViewModel vm) return;

        var files = e.Data.GetFiles();
        if (files != null)
        {
            var paths = files
                .Select(f => f.Path?.LocalPath)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Cast<string>()
                .ToList();
            if (paths.Count > 0)
            {
                e.Handled = true;
                var allImages = paths.All(p => ImageExtensions.Contains(Path.GetExtension(p).ToLowerInvariant()));
                if (allImages)
                {
                    await vm.AddScreenshotsFromPathsAsync(paths);
                }
                else
                {
                    vm.PopulateFromPaths(paths);
                }
            }
        }
    }
}
