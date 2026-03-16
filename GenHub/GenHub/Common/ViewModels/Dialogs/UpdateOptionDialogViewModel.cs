using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Models.Dialogs;
using GenHub.Core.Models.Enums;

namespace GenHub.Common.ViewModels.Dialogs;

/// <summary>
/// ViewModel for the update option dialog.
/// </summary>
public partial class UpdateOptionDialogViewModel : ViewModelBase
{
    /// <summary>
    /// Gets or sets the title of the dialog.
    /// </summary>
    [ObservableProperty]
    private string _title = string.Empty;

    /// <summary>
    /// Gets or sets the message displayed in the dialog.
    /// </summary>
    [ObservableProperty]
    private string _message = string.Empty;

    /// <summary>
    /// Gets or sets the default update strategy.
    /// </summary>
    [ObservableProperty]
    private UpdateStrategy _strategy = UpdateStrategy.ReplaceCurrent;

    /// <summary>
    /// Gets or sets a value indicating whether the user selected "Replace Current Version".
    /// </summary>
    public bool IsReplaceCurrentVersion
    {
        get => Strategy == UpdateStrategy.ReplaceCurrent;
        set
        {
            if (value)
            {
                Strategy = UpdateStrategy.ReplaceCurrent;
            }

            OnPropertyChanged(nameof(IsReplaceCurrentVersion));
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the user selected "Create New Profile".
    /// </summary>
    public bool IsCreateNewProfile
    {
        get => Strategy == UpdateStrategy.CreateNewProfile;
        set
        {
            if (value)
            {
                Strategy = UpdateStrategy.CreateNewProfile;
            }

            OnPropertyChanged(nameof(IsCreateNewProfile));
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the "Do not ask again" checkbox is checked.
    /// </summary>
    [ObservableProperty]
    private bool _isDoNotAskAgain;

    /// <summary>
    /// Gets the result of the dialog.
    /// </summary>
    public UpdateDialogResult? Result { get; private set; }

    /// <summary>
    /// Gets or sets the action to execute when the dialog closes.
    /// </summary>
    public System.Action<UpdateDialogResult?>? CloseAction { get; set; }

    /// <summary>
    /// Called when the <see cref="Strategy"/> property changes.
    /// </summary>
    /// <param name="value">The new strategy value.</param>
    partial void OnStrategyChanged(UpdateStrategy value)
    {
        OnPropertyChanged(nameof(IsReplaceCurrentVersion));
        OnPropertyChanged(nameof(IsCreateNewProfile));
    }

    /// <summary>
    /// Handles the Update button click.
    /// </summary>
    [RelayCommand]
    private void Update()
    {
        Result = new UpdateDialogResult
        {
            Action = "Update",
            Strategy = Strategy,
            IsDoNotAskAgain = IsDoNotAskAgain,
        };
        CloseAction?.Invoke(Result);
    }

    /// <summary>
    /// Handles the Skip button click.
    /// </summary>
    [RelayCommand]
    private void Skip()
    {
        Result = new UpdateDialogResult
        {
            Action = "Skip",
            Strategy = Strategy,
            IsDoNotAskAgain = IsDoNotAskAgain,
        };
        CloseAction?.Invoke(Result);
    }
}
