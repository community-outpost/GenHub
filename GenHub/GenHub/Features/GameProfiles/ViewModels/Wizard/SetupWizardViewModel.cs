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
/// Opens on a summary of the installed games and offers the optional components on a customize step.
/// </summary>
/// <param name="items">The initial list of setup items.</param>
/// <param name="localizationService">The localization service used to resolve labels, or null to use English.</param>
/// <param name="foundGames">The display names of the installed games that were found.</param>
public sealed partial class SetupWizardViewModel(
    IEnumerable<SetupWizardItemViewModel> items,
    ILocalizationService? localizationService = null,
    IReadOnlyList<string>? foundGames = null) : ViewModelBase, IRequestCloseViewModel
{
    [ObservableProperty]
    private ObservableCollection<SetupWizardItemViewModel> _items = new(items);

    /// <summary>
    /// Gets or sets the title of the customize step.
    /// </summary>
    [ObservableProperty]
    private string _title = localizationService.GetWizardText(GameClientConstants.WizardLocalizationKeys.Title);

    /// <summary>
    /// Gets or sets the label for the confirm/continue button.
    /// </summary>
    [ObservableProperty]
    private string _confirmLabel = FormatConfirmLabel(localizationService, items.Count(x => x.IsSelected));

    /// <summary>
    /// Gets or sets a value indicating whether the customize step is shown instead of the summary.
    /// </summary>
    [ObservableProperty]
    private bool _isCustomizing;

    private bool _confirmed;

    /// <summary>
    /// Event to signal view to close.
    /// </summary>
    public event System.EventHandler? RequestClose;

    /// <summary>
    /// Gets a value indicating whether the user confirmed the setup actions.
    /// </summary>
    public bool Confirmed => _confirmed;

    /// <summary>
    /// Gets a value indicating whether the user chose to use the installed game without optional components.
    /// </summary>
    public bool UseInstalledGameChosen { get; private set; }

    /// <summary>
    /// Gets the installed games that were found.
    /// </summary>
    public IReadOnlyList<string> FoundGames { get; } = foundGames ?? [];

    /// <summary>
    /// Gets a value indicating whether more than one installed game was found.
    /// </summary>
    public bool HasMultipleFoundGames => FoundGames.Count > 1;

    /// <summary>
    /// Gets the sentence describing what was found.
    /// </summary>
    public string FoundSummary { get; } = FormatFoundSummary(localizationService, foundGames ?? []);

    private static string FormatConfirmLabel(ILocalizationService? localizationService, int selectedCount) =>
        selectedCount > 0
            ? localizationService.GetWizardText(GameClientConstants.WizardLocalizationKeys.ContinueWithCount, selectedCount)
            : localizationService.GetWizardText(GameClientConstants.WizardLocalizationKeys.Continue);

    private static string FormatFoundSummary(ILocalizationService? localizationService, IReadOnlyList<string> foundGames) =>
        foundGames.Count switch
        {
            0 => localizationService.GetWizardText(GameClientConstants.WizardLocalizationKeys.FoundNoNamedGame),
            1 => localizationService.GetWizardText(GameClientConstants.WizardLocalizationKeys.FoundOneGame, foundGames[0]),
            _ => localizationService.GetWizardText(GameClientConstants.WizardLocalizationKeys.FoundManyGames),
        };

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
    private void UseInstalledGame()
    {
        UseInstalledGameChosen = true;
        _confirmed = true;
        OnRequestClose();
    }

    [RelayCommand]
    private void Customize() => IsCustomizing = true;

    [RelayCommand]
    private void Back() => IsCustomizing = false;

    [RelayCommand]
    private void Confirm()
    {
        UseInstalledGameChosen = false;
        _confirmed = true;
        OnRequestClose();
    }

    [RelayCommand]
    private void SetUpLater()
    {
        UseInstalledGameChosen = false;
        _confirmed = false;
        OnRequestClose();
    }

    private void UpdateLabels()
    {
        ConfirmLabel = FormatConfirmLabel(localizationService, Items.Count(x => x.IsSelected));
    }

    private void OnRequestClose() => RequestClose?.Invoke(this, System.EventArgs.Empty);
}
