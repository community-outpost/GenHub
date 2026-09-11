using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GenHub.Features.Tools.GenHotkeys.ViewModels;

namespace GenHub.Features.Tools.GenHotkeys.Views;

/// <summary>
/// Interaction logic for GenHotkeysView.axaml.
/// </summary>
public partial class GenHotkeysView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GenHotkeysView"/> class.
    /// </summary>
    public GenHotkeysView()
    {
        InitializeComponent();
    }

    private void OnKeyButtonClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && tag.Length > 0 && DataContext is GenHotkeysViewModel vm)
        {
            _ = vm.AssignHotkeyAsync(tag[0]);
        }
    }

    private void OnRootKeyDown(object? sender, KeyEventArgs e)
    {
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
