using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using GenHub.Core.Models.Tools.ReplayManager;
using GenHub.Features.Tools.ReplayManager.ViewModels;
using System;
using System.IO;
using System.Linq;

namespace GenHub.Features.Tools.ReplayManager.Views;

/// <summary>
/// Interaction logic for <see cref="ReplayManagerView"/>.
/// </summary>
public partial class ReplayManagerView : UserControl
{
    private Border? _dragDropOverlay;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReplayManagerView"/> class.
    /// </summary>
    public ReplayManagerView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, DragOver);
        AddHandler(DragDrop.DragLeaveEvent, DragLeave);
        AddHandler(DragDrop.DropEvent, Drop);

        var dataGrid = this.Find<DataGrid>("ReplaysGrid");
        if (dataGrid != null)
        {
            dataGrid.SelectionChanged += OnSelectionChanged;

            // CellEditEnded is handled via XAML, but can also be attached here if needed.
        }
    }

    /// <summary>
    /// Handles the cell edit ended event for the data grid.
    /// </summary>
    /// <param name="sender">The sender of the event.</param>
    /// <param name="e">The event arguments.</param>
    public void OnCellEditEnded(object? sender, DataGridCellEditEndedEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Commit && e.Row.DataContext is ReplayFile replay)
        {
            // The FileName property is updated by the binding before this event fires.
            // replay.FullPath contains the original path.
            var oldPath = replay.FullPath;
            var directory = Path.GetDirectoryName(oldPath);
            if (directory == null) return;

            var newFileName = replay.FileName;

            // Ensure .rep extension if missing?
            if (!newFileName.EndsWith(".rep", StringComparison.OrdinalIgnoreCase))
            {
                newFileName += ".rep";
            }

            var newPath = Path.Combine(directory, newFileName);

            if (string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                File.Move(oldPath, newPath);
                replay.FullPath = newPath;
                replay.FileName = newFileName; // Ensure case or extension is normalized
            }
            catch (IOException)
            {
                // File exists or other IO error
                replay.FileName = Path.GetFileName(oldPath);
            }
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _dragDropOverlay = this.Find<Border>("DragDropOverlay");
    }

    private void DragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains(DataFormats.Files))
        {
            var files = e.Data.GetFiles();
            bool hasValidFiles = files != null && files.Any(f =>
            {
                var path = f.Path.LocalPath;
                return path.EndsWith(".rep", StringComparison.OrdinalIgnoreCase) ||
                       path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
            });

            if (hasValidFiles)
            {
                e.DragEffects = DragDropEffects.Copy;
                if (_dragDropOverlay != null)
                {
                    _dragDropOverlay.IsVisible = true;
                    _dragDropOverlay.Opacity = 1.0;
                }
            }
            else
            {
                e.DragEffects = DragDropEffects.None;
            }
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private void DragLeave(object? sender, RoutedEventArgs e)
    {
        if (_dragDropOverlay != null)
        {
            _dragDropOverlay.Opacity = 0.0;

            // We use a small delay or just wait for transition? IsVisible=false breaks transition if immediate.
            // But IsVisible=false is needed when fully hidden to not block input.
            // Actually hit test is off, so it should be fine.
            // Better to hide IsVisible after transition or just leave it visible but Opacity 0?
            // Let's just set Opacity 0 for now.
        }
    }

    private async void Drop(object? sender, DragEventArgs e)
    {
        if (_dragDropOverlay != null)
        {
            _dragDropOverlay.Opacity = 0.0;
            _dragDropOverlay.IsVisible = false;
        }

        if (e.Data.Contains(DataFormats.Files))
        {
            var files = e.Data.GetFiles();
            if (files != null && DataContext is ReplayManagerViewModel vm)
            {
                var filePaths = files.Select(f => f.Path.LocalPath).ToList();
                await vm.ImportFilesAsync(filePaths);
            }
        }
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is DataGrid dg && DataContext is ReplayManagerViewModel vm)
        {
            var selected = dg.SelectedItems.OfType<ReplayFile>().ToList();
            vm.UpdateSelectedReplays(selected);
        }
    }
}
