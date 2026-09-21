using Avalonia.Controls;
using GenHub.Features.Tools.WndEditor.ViewModels;
using System;

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

    private void OnColorFlyoutOpened(object? sender, EventArgs e)
    {
        _ = e;
        GetRgbaViewModel(sender)?.BeginColorEdit();
    }

    private void OnColorFlyoutClosed(object? sender, EventArgs e)
    {
        _ = e;
        GetRgbaViewModel(sender)?.EndColorEdit();
    }
}
