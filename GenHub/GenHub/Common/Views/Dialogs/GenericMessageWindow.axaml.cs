using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using GenHub.Common.ViewModels.Dialogs;
using System;
#if DEBUG
using Avalonia.Diagnostics;
#endif

namespace GenHub.Common.Views.Dialogs;

/// <summary>
/// Interaction logic for GenericMessageWindow.axaml.
/// </summary>
public partial class GenericMessageWindow : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GenericMessageWindow"/> class.
    /// </summary>
    public GenericMessageWindow()
    {
        InitializeComponent();
    }

    /// <inheritdoc/>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is GenericMessageViewModel vm)
        {
            vm.CloseRequested += Close;
        }
    }

    /// <inheritdoc/>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        // Allow dragging the window from anywhere essentially
        BeginMoveDrag(e);
    }

    /// <inheritdoc/>
    /// <param name="e">The key event arguments.</param>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && !e.Handled)
        {
            e.Handled = true;
            Close();
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
