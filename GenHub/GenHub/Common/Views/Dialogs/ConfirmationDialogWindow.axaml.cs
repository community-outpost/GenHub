using Avalonia.Controls;
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

    /// <summary>
    /// Loads and initializes the XAML components for this window.
    /// </summary>
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
