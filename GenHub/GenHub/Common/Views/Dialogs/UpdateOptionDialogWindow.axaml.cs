using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GenHub.Common.ViewModels.Dialogs;
using System;

namespace GenHub.Common.Views.Dialogs;

/// <summary>
/// Window for displaying update options to the user.
/// </summary>
public partial class UpdateOptionDialogWindow : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateOptionDialogWindow"/> class.
    /// </summary>
    public UpdateOptionDialogWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Called when the window is opened.
    /// </summary>
    /// <param name="e">The event arguments.</param>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is UpdateOptionDialogViewModel vm)
        {
            vm.CloseAction = (result) => Close(result);
        }
    }

    /// <summary>
    /// Initializes the component.
    /// </summary>
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
