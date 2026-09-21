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

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains(DataFormats.Files))
        {
            return;
        }

        if (DataContext is not AddContentDialogViewModel vm) return;

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
                vm.PopulateFromPaths(paths);
                e.Handled = true;
            }
        }
    }
}
