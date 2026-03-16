using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using GenHub.Core.Models.Tools.MapManager;
using GenHub.Features.Tools.MapManager.ViewModels;
using System;
using System.IO;
using System.Linq;

namespace GenHub.Features.Tools.MapManager.Views;

/// <summary>
/// Code-behind for MapManagerView.
/// </summary>
public partial class MapManagerView : UserControl
{
    private Border? _dragDropOverlay;

    /// <summary>
    /// Initializes a new instance of the <see cref="MapManagerView"/> class.
    /// </summary>
    public MapManagerView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, DragOver);
        AddHandler(DragDrop.DragLeaveEvent, DragLeave);
        AddHandler(DragDrop.DropEvent, Drop);

        var dataGrid = this.Find<DataGrid>("MapsGrid");
        if (dataGrid != null)
        {
            dataGrid.SelectionChanged += OnSelectionChanged;
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
                return Directory.Exists(path) ||
                       path.EndsWith(".map", StringComparison.OrdinalIgnoreCase) ||
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

                e.Handled = true;
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
            _dragDropOverlay.IsVisible = false;
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
            if (files != null && DataContext is MapManagerViewModel vm)
            {
                var filePaths = files.Select(f => f.Path.LocalPath).ToList();
                await vm.ImportFilesAsync(filePaths);
            }

            e.Handled = true;
        }
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is DataGrid dg && DataContext is MapManagerViewModel vm)
        {
            var selected = dg.SelectedItems.OfType<MapFile>().ToList();
            vm.UpdateSelectedMaps(selected);
        }
    }
}
