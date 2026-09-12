using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GenHub.Features.Tools.GenHotkeys.ViewModels;

namespace GenHub.Features.Tools.GenHotkeys.Views;

/// <summary>
/// Code-behind for the GenHotkeys main tool view.
/// Supports keyboard-based hotkey assignment: when an action is selected,
/// pressing an alphanumeric key immediately assigns it, Backspace/Delete clears it.
/// </summary>
public partial class GenHotkeysView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GenHotkeysView"/> class.
    /// </summary>
    public GenHotkeysView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnRootKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnKeyButtonClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Content is string text && text.Length == 1 &&
            DataContext is GenHotkeysViewModel vm)
        {
            vm.AssignHotkey(text[0]);
        }
    }

    private void OnRootKeyDown(object? sender, KeyEventArgs e)
    {
        // Ignore key combinations with modifiers (Ctrl, Alt, Shift, Meta) so app shortcuts aren't swallowed
        if (e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        // Do not intercept keystrokes when the user is typing in a text input field (e.g. naming/renaming a profile)
        if (e.Source is TextBox or AutoCompleteBox ||
            TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox or AutoCompleteBox)
        {
            return;
        }

        if (DataContext is not GenHotkeysViewModel vm || vm.SelectedAction == null)
        {
            return;
        }

        // If user presses Escape or Delete / Backspace
        if (e.Key is Key.Back or Key.Delete)
        {
            _ = vm.ClearHotkeyAsync();
            e.Handled = true;
            return;
        }

        // Check alphanumeric keys
        if (e.Key is >= Key.A and <= Key.Z)
        {
            var ch = (char)('A' + (e.Key - Key.A));
            _ = vm.AssignHotkeyAsync(ch);
            e.Handled = true;
        }
        else if (e.Key is >= Key.D0 and <= Key.D9)
        {
            var ch = (char)('0' + (e.Key - Key.D0));
            _ = vm.AssignHotkeyAsync(ch);
            e.Handled = true;
        }
    }
}
