using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GenHub.Common.Controls;

/// <summary>
/// Shared file drag/drop wiring for views: enables drops, shows the copy cursor
/// over file drags, and extracts dropped local paths.
/// </summary>
public static class ViewDropHelper
{
    /// <summary>
    /// Enables file drops on a control with the shared drag-over cursor handling.
    /// </summary>
    /// <param name="control">The control accepting drops.</param>
    /// <param name="onDrop">The drop handler.</param>
    public static void EnableFileDrop(Control control, EventHandler<DragEventArgs> onDrop)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(onDrop);

        DragDrop.SetAllowDrop(control, true);
        control.AddHandler(DragDrop.DragOverEvent, OnDragOver, handledEventsToo: true);
        control.AddHandler(DragDrop.DropEvent, onDrop, handledEventsToo: true);
    }

    /// <summary>
    /// Extracts local file paths from a drop event.
    /// </summary>
    /// <param name="e">The drag event arguments.</param>
    /// <returns>The dropped local paths, or an empty list when none apply.</returns>
    public static List<string> ExtractDroppedPaths(DragEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        return e.Data.GetFiles()?
            .Select(f => f.Path?.LocalPath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Cast<string>()
            .ToList() ?? [];
    }

    /// <summary>
    /// Checks whether a visual sits inside the named control subtree.
    /// </summary>
    /// <param name="visual">The visual to test.</param>
    /// <param name="name">The ancestor control name.</param>
    /// <returns>True when an ancestor has the given name.</returns>
    public static bool IsInSubtree(Visual? visual, string name)
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
}
