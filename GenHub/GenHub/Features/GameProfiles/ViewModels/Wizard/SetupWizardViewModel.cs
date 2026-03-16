using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Common.ViewModels;

namespace GenHub.Features.GameProfiles.ViewModels.Wizard;

/// <summary>
/// ViewModel for the Setup Wizard dialog.
/// Manages the list of setup items and user confirmation.
/// </summary>
/// <param name="items">The initial list of setup items.</param>
public sealed partial class SetupWizardViewModel(IEnumerable<SetupWizardItemViewModel> items) : ViewModelBase
{
    [ObservableProperty]
    private ObservableCollection<SetupWizardItemViewModel> _items = new(items);

    /// <summary>
    /// Gets or sets the title of the wizard window.
    /// </summary>
    [ObservableProperty]
    private string _title = "Setup Detected Content";

    /// <summary>
    /// Gets or sets the label for the cancel/skip button.
    /// </summary>
    [ObservableProperty]
    private string _cancelLabel = "Skip & Create Base Profiles";

    /// <summary>
    /// Gets or sets the label for the confirm/continue button.
    /// </summary>
    [ObservableProperty]
    private string _confirmLabel = items.Any(x => x.IsSelected)
        ? $"Continue ({items.Count(x => x.IsSelected)})"
        : "Continue";

    private bool _confirmed = false;

    /// <summary>
    /// Gets a value indicating whether the user confirmed the setup actions.
    /// </summary>
    public bool Confirmed => _confirmed;

    [RelayCommand]
    private void ToggleSelection(SetupWizardItemViewModel item)
    {
        if (!item.IsMandatory)
        {
            // IsSelected is bound two-way, so we just need to update the summary labels
            UpdateLabels();
        }
    }

    [RelayCommand]
    private void Confirm()
    {
        _confirmed = true;

        // Close window logic will be handled by the View's close handler binding to this command or interaction
        OnCloseRequested();
    }

    [RelayCommand]
    private void Cancel()
    {
        _confirmed = false;
        OnCloseRequested();
    }

    private void UpdateLabels()
    {
        var selectedCount = Items.Count(x => x.IsSelected);
        ConfirmLabel = selectedCount > 0 ? $"Continue ({selectedCount})" : "Continue";
    }

    /// <summary>
    /// Event to signal view to close.
    /// </summary>
    public event System.EventHandler? CloseRequested;

    private void OnCloseRequested() => CloseRequested?.Invoke(this, System.EventArgs.Empty);
}
