using Avalonia.Controls;
using Avalonia.Input;
using GenHub.Features.Tools.ViewModels;
using System;
using System.Diagnostics;
using System.Linq;

namespace GenHub.Features.Tools.Views.PublisherStudio;

/// <summary>
/// View for managing content library items.
/// </summary>
public partial class ContentLibraryView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ContentLibraryView"/> class.
    /// </summary>
    public ContentLibraryView()
    {
        InitializeComponent();
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver, handledEventsToo: true);
        AddHandler(DragDrop.DropEvent, OnDrop, handledEventsToo: true);

        var contentItemsDropZone = this.FindControl<Border>("ContentItemsDropZone");
        if (contentItemsDropZone != null)
        {
            contentItemsDropZone.AddHandler(DragDrop.DragOverEvent, OnDragOver, handledEventsToo: true);
            contentItemsDropZone.AddHandler(DragDrop.DropEvent, OnContentItemsDrop, handledEventsToo: true);
        }

        var addonsDropZone = this.FindControl<Border>("AddonsDropZone");
        if (addonsDropZone != null)
        {
            addonsDropZone.AddHandler(DragDrop.DragOverEvent, OnDragOver, handledEventsToo: true);
            addonsDropZone.AddHandler(DragDrop.DropEvent, OnAddonsDrop, handledEventsToo: true);
        }

        var releasesDropZone = this.FindControl<Border>("ReleasesDropZone");
        if (releasesDropZone != null)
        {
            releasesDropZone.AddHandler(DragDrop.DragOverEvent, OnDragOver, handledEventsToo: true);
            releasesDropZone.AddHandler(DragDrop.DropEvent, OnReleasesDrop, handledEventsToo: true);
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

    private async void OnContentItemsDrop(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains(DataFormats.Files)) return;
        if (DataContext is not ContentLibraryViewModel vm) return;

        try
        {
            var files = e.Data.GetFiles();
            if (files != null)
            {
                var first = files.FirstOrDefault();
                if (first?.Path?.LocalPath is { } path)
                {
                    e.Handled = true;
                    await vm.AddContentWithPathAsync(path);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to add dropped content: {ex}");
        }
    }

    private async void OnAddonsDrop(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains(DataFormats.Files)) return;
        if (DataContext is not ContentLibraryViewModel vm) return;

        try
        {
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
                    await vm.AddAddonWithPathsAsync(paths);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to add dropped addon: {ex}");
        }
    }

    private async void OnReleasesDrop(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains(DataFormats.Files)) return;
        if (DataContext is not ContentLibraryViewModel vm) return;

        try
        {
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
                    await vm.AddReleaseWithPathsAsync(paths);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to add dropped release: {ex}");
        }
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.Handled || !e.Data.Contains(DataFormats.Files))
        {
            return;
        }

        if (DataContext is not ContentLibraryViewModel vm) return;

        try
        {
            var files = e.Data.GetFiles();
            if (files != null)
            {
                var first = files.FirstOrDefault();
                if (first?.Path?.LocalPath is { } path)
                {
                    e.Handled = true;
                    await vm.AddContentWithPathAsync(path);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to add dropped content: {ex}");
        }
    }
}
