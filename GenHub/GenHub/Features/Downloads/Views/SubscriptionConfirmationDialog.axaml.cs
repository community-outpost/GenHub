using Avalonia.Interactivity;
using GenHub.Common.Controls;
using GenHub.Features.Content.ViewModels.Catalog;
using System;
using System.Threading;

namespace GenHub.Features.Downloads.Views;

/// <summary>
/// interaction logic for SubscriptionConfirmationDialog.axaml.
/// </summary>
public partial class SubscriptionConfirmationDialog : GenHubWindow
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
    /// Gets a value indicating whether the dialog was accepted.
    /// </summary>
    public bool DialogResult { get; private set; }

    /// <summary>
    /// Closes the dialog with the specified result.
    /// </summary>
    /// <param name="result">The result to return from the dialog.</param>
    public void CloseDialog(bool result)
    {
        DialogResult = result;
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
            // set up a way to close the window from the view model while preserving external handlers
            var externalRequestClose = vm.RequestClose;
            vm.RequestClose = (result) =>
            {
                DialogResult = result;
                Close(result);
                externalRequestClose?.Invoke(result);
            };

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

    /// <inheritdoc/>
    protected override void OnEscapePressed() => CloseDialog(false);

    /// <inheritdoc/>
    protected override bool TitleBarDoubleClickMaximizes => false;

    /// <inheritdoc/>
    protected override void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close(false);
    }
}
