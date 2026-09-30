using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Common.ViewModels;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace GenHub.Features.GameProfiles.ViewModels.Wizard;

/// <summary>
/// ViewModel for the Setup Wizard dialog.
/// Manages the list of setup items and user confirmation.
/// </summary>
/// <param name="items">The initial list of setup items.</param>
/// <param name="localizationService">The localization service used to resolve labels, or null to use English.</param>
public sealed partial class SetupWizardViewModel(IEnumerable<SetupWizardItemViewModel> items, ILocalizationService? localizationService = null) : ViewModelBase, IRequestCloseViewModel
{
    [ObservableProperty]
    private ObservableCollection<SetupWizardItemViewModel> _items = new(items);

    /// <summary>
    /// Gets or sets the title of the wizard window.
    /// </summary>
    [ObservableProperty]
    private string _title = localizationService.GetWizardText(GameClientConstants.WizardLocalizationKeys.Title);

    /// <summary>
    /// Gets or sets the label for the cancel/skip button.
    /// </summary>
    [ObservableProperty]
    private string _cancelLabel = localizationService.GetWizardText(GameClientConstants.WizardLocalizationKeys.Skip);

    /// <summary>
    /// Gets or sets the label for the confirm/continue button.
    /// </summary>
    [ObservableProperty]
    private string _confirmLabel = FormatConfirmLabel(localizationService, items.Count(x => x.IsSelected));

    private bool _confirmed = false;

    /// <summary>
    /// Gets a value indicating whether the user confirmed the setup actions.
    /// </summary>
    public bool Confirmed => _confirmed;

    private static string FormatConfirmLabel(ILocalizationService? localizationService, int selectedCount) =>
        selectedCount > 0
            ? localizationService.GetWizardText(GameClientConstants.WizardLocalizationKeys.ContinueWithCount, selectedCount)
            : localizationService.GetWizardText(GameClientConstants.WizardLocalizationKeys.Continue);

    [RelayCommand]
    private void ToggleSelection(SetupWizardItemViewModel? item)
    {
        if (item == null)
        {
            return;
        }

        if (!item.IsMandatory)
        {
            item.IsSelected = !item.IsSelected;
            UpdateLabels();
        }
    }

    [RelayCommand]
    private void Confirm()
    {
        _confirmed = true;

        // Close window logic will be handled by the View's close handler binding to this command or interaction
        OnRequestClose();
    }

    [RelayCommand]
    private void Cancel()
    {
        _confirmed = false;
        OnRequestClose();
    }

    private void UpdateLabels()
    {
        ConfirmLabel = FormatConfirmLabel(localizationService, Items.Count(x => x.IsSelected));
    }

    /// <summary>
    /// Event to signal view to close.
    /// </summary>
    public event System.EventHandler? RequestClose;

    private void OnRequestClose() => RequestClose?.Invoke(this, System.EventArgs.Empty);
}
