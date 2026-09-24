using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using GenHub.Features.Tools.ViewModels;
using System;
using System.Diagnostics;
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

    private static bool IsInSubtree(Visual? visual, string name)
    {
        while (visual != null)
        {
            if (visual is Control control && control.Name == name)
            {
                return true;
            }

            visual = visual.GetVisualParent();
        }

        return false;
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        var visual = e.Source as Visual;
        if ((IsInSubtree(visual, "AvatarDropTarget") || IsInSubtree(visual, "AvatarTextBox")) &&
            (e.Data.Contains(DataFormats.Files) || e.Data.Contains(DataFormats.Text)))
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
        try
        {
            if (DataContext is not PublisherProfileViewModel vm)
            {
                return;
            }

            var sourceVisual = e.Source as Visual;
            if (!IsInSubtree(sourceVisual, "AvatarDropTarget") && !IsInSubtree(sourceVisual, "AvatarTextBox"))
            {
                return;
            }

            if (e.Data.Contains(DataFormats.Files))
            {
                var files = e.Data.GetFiles()?.Select(f => f.Path?.LocalPath).Where(p => !string.IsNullOrEmpty(p)).ToList();
                if (files != null && files.Count > 0 && files[0] is { } filePath)
                {
                    e.Handled = true;
                    await vm.HandleAvatarDropAsync(filePath);
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
        catch (Exception ex)
        {
            Debug.WriteLine($"PublisherProfileView: drop handling failed: {ex.Message}");
        }
    }
}
