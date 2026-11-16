using Avalonia.Controls;
using GenHub.Features.Tools.WndEditor.ViewModels;
using System;
using System.Diagnostics.CodeAnalysis;

namespace GenHub.Features.Tools.WndEditor.Views;

/// <summary>
/// General properties tab of the WND editor.
/// </summary>
public partial class WndGeneralPropertiesView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WndGeneralPropertiesView"/> class.
    /// </summary>
    public WndGeneralPropertiesView()
    {
        InitializeComponent();
    }

    private static WndRgbaViewModel? GetRgbaViewModel(object? sender)
    {
        return sender is Flyout flyout && flyout.Content is Control content
            ? content.DataContext as WndRgbaViewModel
            : null;
    }

    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Avalonia XAML event handler")]
    private void OnColorFlyoutOpened(object? sender, EventArgs e)
    {
        _ = e;
        GetRgbaViewModel(sender)?.BeginColorEdit();
    }

    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Avalonia XAML event handler")]
    private void OnColorFlyoutClosed(object? sender, EventArgs e)
    {
        _ = e;
        GetRgbaViewModel(sender)?.EndColorEdit();
    }
}
