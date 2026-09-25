using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using GenHub.Common.Helpers;
using GenHub.Features.Tools.ViewModels.Dialogs;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.Views.Dialogs;

/// <summary>
/// Code-behind for RenameCatalogDialogView with unified clipboard paste and drag-drop support.
/// </summary>
public partial class RenameCatalogDialogView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RenameCatalogDialogView"/> class.
    /// </summary>
    public RenameCatalogDialogView()
    {
        InitializeComponent();

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(KeyDownEvent, OnKeyDownTunnel, Avalonia.Interactivity.RoutingStrategies.Tunnel);
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
        if (DataContext is not RenameCatalogDialogViewModel vm)
        {
            return;
        }

        e.Handled = true;
        if (e.Data.Contains(DataFormats.Files))
        {
            var files = e.Data.GetFiles();
            var first = files?.FirstOrDefault();
            if (first != null && !string.IsNullOrWhiteSpace(first.Path.LocalPath))
            {
                await vm.HandleIconDropAsync(first.Path.LocalPath);
                return;
            }
        }

        if (e.Data.Contains(DataFormats.Text))
        {
            var text = e.Data.GetText();
            if (!string.IsNullOrWhiteSpace(text))
            {
                await vm.HandleIconDropAsync(text.Trim());
            }
        }
    }

    private async void OnKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        var isPaste = (e.Key == Key.V && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
                      || (e.Key == Key.Insert && e.KeyModifiers.HasFlag(KeyModifiers.Shift));

        if (!isPaste || DataContext is not RenameCatalogDialogViewModel vm)
        {
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard == null)
        {
            return;
        }

        // If the focused control is the CatalogName TextBox, allow default text pasting if clipboard is plain short text (not an image/file)
        if (topLevel.FocusManager?.GetFocusedElement() is TextBox tb && tb.Name != "InputTextBox")
        {
            var formats = (await topLevel.Clipboard.GetFormatsAsync()) ?? [];
            var hasImage = formats.Any(f => f.Contains("image", System.StringComparison.OrdinalIgnoreCase)
                                         || f.Contains("png", System.StringComparison.OrdinalIgnoreCase)
                                         || f.Contains("bitmap", System.StringComparison.OrdinalIgnoreCase)
                                         || f.Contains("dib", System.StringComparison.OrdinalIgnoreCase));
            if (!hasImage && !formats.Contains(DataFormats.Files))
            {
                return; // Let standard TextBox handle text paste
            }
        }

        var pathOrUrl = await ClipboardInputHelper.ExtractPastedFileOrImageAsync(topLevel.Clipboard);
        if (!string.IsNullOrWhiteSpace(pathOrUrl))
        {
            e.Handled = true;
            await vm.HandleIconDropAsync(pathOrUrl);
        }
    }
}
