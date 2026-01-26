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
    /// <summary>
    /// Initializes a new SubscriptionConfirmationDialog instance and loads its UI components.
    /// </summary>
    public SubscriptionConfirmationDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Closes the dialog with the specified result.
    /// </summary>
    /// <summary>
    /// Closes the dialog and sets its dialog result to the specified value.
    /// </summary>
    /// <param name="result">The boolean result to assign to the dialog.</param>
    public void CloseDialog(bool result)
    {
        Close(result);
    }

    /// <summary>
    /// Called when the window is opened.
    /// </summary>
    /// <summary>
    /// Handles the window opened event by wiring the view model's close delegate and invoking its asynchronous initialization if the DataContext is a SubscriptionConfirmationViewModel.
    /// </summary>
    /// <param name="e">The event arguments for the opened event.</param>
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