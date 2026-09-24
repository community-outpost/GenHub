using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using GenHub.Features.Tools.ViewModels;
using System.IO;
using System.Linq;

namespace GenHub.Features.Tools.Views.PublisherStudio;

/// <summary>
/// View for editing publisher profile information.
/// </summary>
public partial class PublisherProfileView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PublisherProfileView"/> class.
    /// </summary>
    public PublisherProfileView()
    {
        InitializeComponent();
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver, handledEventsToo: true);
        AddHandler(DragDrop.DropEvent, OnDrop, handledEventsToo: true);
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains(DataFormats.Files) || e.Data.Contains(DataFormats.Text))
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
        if (DataContext is not PublisherProfileViewModel vm)
        {
            return;
        }

        if (e.Data.Contains(DataFormats.Files))
        {
            var files = e.Data.GetFiles()?.Select(f => f.Path?.LocalPath).Where(p => !string.IsNullOrEmpty(p)).ToList();
            if (files != null && files.Count > 0)
            {
                e.Handled = true;
                await vm.HandleAvatarDropAsync(files[0]!);
                return;
            }
        }

        if (e.Data.Contains(DataFormats.Text))
        {
            var text = e.Data.GetText();
            if (!string.IsNullOrWhiteSpace(text))
            {
                e.Handled = true;
                await vm.HandleAvatarDropAsync(text.Trim());
            }
        }
    }
}
