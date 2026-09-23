using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
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
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
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

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.Handled || !e.Data.Contains(DataFormats.Files))
        {
            return;
        }

        if (DataContext is not AddContentDialogViewModel vm)
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

        if (paths.Count == 0)
        {
            return;
        }

        var sourceVisual = e.Source as Visual;

        // Dropped specifically on Icon field
        if (IsInSubtree(sourceVisual, "IconDropTarget") || IsInSubtree(sourceVisual, "IconTextBox"))
        {
            e.Handled = true;
            vm.IconArtwork = paths[0];
            return;
        }

        // Dropped specifically on Banner field
        if (IsInSubtree(sourceVisual, "BannerDropTarget") || IsInSubtree(sourceVisual, "BannerTextBox"))
        {
            e.Handled = true;
            vm.BannerArtwork = paths[0];
            return;
        }

        // Dropped specifically on Backdrop field
        if (IsInSubtree(sourceVisual, "BackdropDropTarget") || IsInSubtree(sourceVisual, "BackdropTextBox"))
        {
            e.Handled = true;
            vm.BackdropArtwork = paths[0];
            return;
        }

        // Dropped specifically on Screenshots drop zone
        if (IsInSubtree(sourceVisual, "ContentMediaDropZone"))
        {
            e.Handled = true;
            await vm.AddScreenshotsFromPathsAsync(paths);
            return;
        }

        // Dropped specifically on Initial Release drop zone or section
        if (IsInSubtree(sourceVisual, "InitialReleaseDropZone") || IsInSubtree(sourceVisual, "InitialReleaseSection"))
        {
            e.Handled = true;
            await vm.AddReleaseArtifactsFromPathsAsync(paths);
            return;
        }

        // Fallback for drops elsewhere on the dialog:
        e.Handled = true;
        var allImages = paths.All(p => ImageExtensions.Contains(Path.GetExtension(p).ToLowerInvariant()));
        if (allImages)
        {
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
        else
        {
            vm.PopulateFromPaths(paths);
        }
    }

    private bool IsInSubtree(Visual? visual, string name)
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
}
