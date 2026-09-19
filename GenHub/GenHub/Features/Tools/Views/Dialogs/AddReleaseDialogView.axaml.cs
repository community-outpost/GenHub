using Avalonia.Controls;
using Avalonia.Input;
using GenHub.Features.Tools.ViewModels.Dialogs;
using System.Linq;

namespace GenHub.Features.Tools.Views.Dialogs;

/// <summary>
/// View for adding a new release.
/// Dropped files and folders are added as release artifacts with heuristically filled metadata.
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

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains(DataFormats.Files))
        {
            return;
        }

        if (DataContext is not AddReleaseDialogViewModel vm)
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
            await vm.AddArtifactsFromPathsAsync(paths);
            e.Handled = true;
        }
    }
}
