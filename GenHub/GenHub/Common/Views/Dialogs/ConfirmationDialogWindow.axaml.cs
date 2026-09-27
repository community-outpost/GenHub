using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using GenHub.Common.ViewModels.Dialogs;

namespace GenHub.Common.Views.Dialogs;

/// <summary>
/// Window for displaying a confirmation dialog.
/// </summary>
public partial class ConfirmationDialogWindow : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConfirmationDialogWindow"/> class.
    /// </summary>
    public ConfirmationDialogWindow()
    {
        InitializeComponent();
    }

    /// <inheritdoc/>
    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ConfirmationDialogViewModel vm)
        {
            vm.CloseAction = Close;
        }
    }

    /// <inheritdoc/>
    /// <param name="e">The key event arguments.</param>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && !e.Handled)
        {
            e.Handled = true;
            Close(false);
        }
    }

    /// <summary>
    /// Loads and initializes the XAML components for this window.
    /// </summary>
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
