using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GenHub.Common.ViewModels.Dialogs;

/// <summary>
/// ViewModel for the confirmation dialog.
/// </summary>
public partial class ConfirmationDialogViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _title = "Confirmation";

    [ObservableProperty]
    private string _message = "Are you sure you want to proceed?";

    [ObservableProperty]
    private string _confirmButtonText = "Confirm";

    [ObservableProperty]
    private string _cancelButtonText = "Cancel";

    [ObservableProperty]
    private bool _showDoNotAskAgain;

    [ObservableProperty]
    private bool _doNotAskAgain;

    /// <summary>
    /// Gets a value indicating whether the dialog was confirmed.
    /// </summary>
    public bool Result { get; private set; }

    /// <summary>
    /// Gets or sets the action to close the dialog window.
    /// </summary>
    public System.Action? CloseAction { get; set; }

    [RelayCommand]
    private void Confirm()
    {
        Result = true;
        CloseAction?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        Result = false;
        CloseAction?.Invoke();
    }
}
