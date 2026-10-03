using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using GenHub.Common.Controls;
using GenHub.Features.Tools.IniEditor.ViewModels;
using System.Windows.Input;

namespace GenHub.Features.Tools.IniEditor.Views;

/// <summary>
/// View for the INI editor tool.
/// </summary>
public partial class IniEditorView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IniEditorView"/> class.
    /// </summary>
    public IniEditorView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    private static bool IsPrimaryShortcut(KeyEventArgs e)
    {
        var isPrimary = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        var hasAlt = (e.KeyModifiers & KeyModifiers.Alt) != 0;
        return isPrimary && !hasAlt;
    }

    private static bool HasShiftModifier(KeyEventArgs e)
    {
        return (e.KeyModifiers & KeyModifiers.Shift) != 0;
    }

    private static ICommand? ResolveShortcutCommand(IniEditorViewModel viewModel, Key key, bool hasShift)
    {
        return (key, hasShift) switch
        {
            (Key.Z, false) => viewModel.UndoCommand,
            (Key.Y, false) => viewModel.RedoCommand,
            (Key.Z, true) => viewModel.RedoCommand,
            (Key.S, false) => viewModel.SaveCommand,
            (Key.S, true) => viewModel.SaveAsCommand,
            _ => null,
        };
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnSubObjectSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var listBox = this.FindControl<ListBox>("SubObjectListBox");
        var viewer = this.FindControl<W3dViewerControl>("PreviewViewer");
        if (listBox == null || viewer == null)
        {
            return;
        }

        if (!listBox.IsFocused && !listBox.IsKeyboardFocusWithin)
        {
            return;
        }

        if (listBox.SelectedItem is W3dPreviewMeshItem selected && selected.MeshIndex >= 0)
        {
            viewer.FocusMesh(selected.MeshIndex);
        }
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not IniEditorViewModel viewModel || !IsPrimaryShortcut(e))
        {
            return;
        }

        var command = ResolveShortcutCommand(viewModel, e.Key, HasShiftModifier(e));
        if (command?.CanExecute(null) == true)
        {
            command.Execute(null);
            e.Handled = true;
        }
    }
}
