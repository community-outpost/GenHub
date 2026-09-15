using System;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GenHub.Features.Content.ViewModels.Catalog;

namespace GenHub.Features.Downloads.Views;

/// <summary>
/// interaction logic for SubscriptionConfirmationDialog.axaml.
/// </summary>
public partial class SubscriptionConfirmationDialog : Window
{
    private readonly CancellationTokenSource _dialogCts = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="SubscriptionConfirmationDialog"/> class.
    /// </summary>
    public SubscriptionConfirmationDialog()
    {
        InitializeComponent();
        Closed += (_, _) =>
        {
            _dialogCts.Cancel();
            _dialogCts.Dispose();
        };
    }

    /// <summary>
    /// closes the dialog with the specified result.
    /// </summary>
    /// <param name="result">the result to return from the dialog.</param>
    public void CloseDialog(bool result)
    {
        Close(result);
    }

    /// <summary>
    /// called when the window is opened.
    /// </summary>
    /// <param name="e">the event arguments.</param>
    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (DataContext is SubscriptionConfirmationViewModel vm)
        {
            // set up a way to close the window from the view model
            vm.RequestClose = (result) => Close(result);

            // start initialization
            try
            {
                await vm.InitializeAsync(_dialogCts.Token);
            }
            catch (OperationCanceledException)
            {
                // Dialog closed during initialization
            }
        }
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(sender as Visual).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
