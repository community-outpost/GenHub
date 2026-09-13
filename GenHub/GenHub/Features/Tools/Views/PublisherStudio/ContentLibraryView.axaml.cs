using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using GenHub.Features.Tools.ViewModels;

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
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not ContentLibraryViewModel vm) return;

        var files = e.Data.GetFiles();
        if (files != null)
        {
            var first = files.FirstOrDefault();
            if (first?.Path?.LocalPath is { } path)
            {
                await vm.AddContentWithPathAsync(path);
            }
        }
    }
}
