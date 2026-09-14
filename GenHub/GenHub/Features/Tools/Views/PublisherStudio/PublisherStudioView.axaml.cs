using System;
using System.Diagnostics;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using GenHub.Features.Tools.ViewModels;

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
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not PublisherStudioViewModel vm)
        {
            return;
        }

        try
        {
            var files = e.Data.GetFiles();
            if (files != null)
            {
                var first = files.FirstOrDefault();
                if (first?.Path?.LocalPath is { } path)
                {
                    e.Handled = true;
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
