using Avalonia.Controls;
using Avalonia.Input;
using GenHub.Features.Tools.ViewModels;
using System;
using System.Diagnostics;
using System.Linq;

namespace GenHub.Features.Tools.Views.PublisherStudio;

/// <summary>
/// Main view for Publisher Studio tool.
/// </summary>
public partial class PublisherStudioView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PublisherStudioView"/> class.
    /// </summary>
    public PublisherStudioView()
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
        if (e.Handled || !e.Data.Contains(DataFormats.Files))
        {
            return;
        }

        if (DataContext is not PublisherStudioViewModel vm)
        {
            return;
        }

        e.Handled = true;

        try
        {
            var files = e.Data.GetFiles();
            if (files != null)
            {
                var first = files.FirstOrDefault();
                if (first?.Path?.LocalPath is { } path)
                {
                    await vm.HandleDroppedPathAsync(path);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to handle dropped path in Publisher Studio: {ex}");
        }
    }
}
