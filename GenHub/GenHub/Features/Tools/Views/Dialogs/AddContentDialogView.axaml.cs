using Avalonia.Controls;
using Avalonia.Input;
using GenHub.Features.Tools.ViewModels.Dialogs;
using System.Linq;

namespace GenHub.Features.Tools.Views.Dialogs;

/// <summary>
/// View for adding new content.
/// </summary>
public partial class AddContentDialogView : UserControl
{
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

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not AddContentDialogViewModel vm) return;

        var files = e.Data.GetFiles();
        if (files != null)
        {
            var first = files.FirstOrDefault();
            if (first?.Path?.LocalPath is { } path)
            {
                vm.PopulateFromPath(path);
            }
        }
    }
}
