using Avalonia.Controls;
using Avalonia.Input;
using GenHub.Features.Tools.ViewModels.Dialogs;
using System;
using System.IO;
using System.Linq;

namespace GenHub.Features.Tools.Views.Dialogs;

/// <summary>
/// View for adding a new artifact.
/// File browsing is handled by BrowseLocalFileCommand in the ViewModel.
/// Dropped files and folders populate the artifact heuristically.
/// </summary>
public partial class AddArtifactDialogView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AddArtifactDialogView"/> class.
    /// </summary>
    public AddArtifactDialogView()
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

        if (DataContext is not AddArtifactDialogViewModel vm)
        {
            return;
        }

        var files = e.Data.GetFiles();
        if (files == null)
        {
            return;
        }

        var first = files.FirstOrDefault();
        if (first?.Path?.LocalPath is { } path)
        {
            try
            {
                await vm.PopulateFromDroppedPathAsync(path);
                e.Handled = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                vm.ValidationError = ex.Message;
                e.Handled = true;
            }
        }
    }
}
