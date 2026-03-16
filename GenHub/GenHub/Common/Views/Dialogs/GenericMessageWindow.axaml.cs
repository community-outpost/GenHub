using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using GenHub.Common.ViewModels.Dialogs;

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
#if DEBUG
        this.AttachDevTools();
#endif
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

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
