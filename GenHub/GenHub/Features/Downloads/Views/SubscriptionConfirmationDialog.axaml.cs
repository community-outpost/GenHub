using System;
using GenHub.Features.Content.ViewModels.Catalog;

namespace GenHub.Features.Downloads.Views;

/// <summary>
/// Interaction logic for SubscriptionConfirmationDialog.axaml.
/// </summary>
public partial class SubscriptionConfirmationDialog : Avalonia.Controls.Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SubscriptionConfirmationDialog"/> class.
    /// </summary>
    public SubscriptionConfirmationDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Closes the dialog with the specified result.
    /// </summary>
    /// <param name="result">The result to return from the dialog.</param>
    public void CloseDialog(bool result)
    {
        Close(result);
    }

    /// <summary>
    /// Called when the window is opened.
    /// </summary>
    /// <param name="e">The event arguments.</param>
    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (DataContext is SubscriptionConfirmationViewModel vm)
        {
            // Set up a way to close the window from the VM
            vm.RequestClose = (result) => Close(result);

            // Start initialization
            await vm.InitializeAsync();
        }
    }
}
