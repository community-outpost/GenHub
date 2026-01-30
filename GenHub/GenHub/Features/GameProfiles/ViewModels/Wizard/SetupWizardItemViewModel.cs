using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;

namespace GenHub.Features.GameProfiles.ViewModels.Wizard;

/// <summary>
/// Represents an item in the Setup Wizard usage flow.
/// </summary>
public partial class SetupWizardItemViewModel : ObservableObject
{
    /// <summary>
    /// Gets or sets the title of the wizard item.
    /// </summary>
    [ObservableProperty]
    private string _title = string.Empty;

    /// <summary>
    /// Gets or sets the description of the wizard item.
    /// </summary>
    [ObservableProperty]
    private string _description = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the item is selected.
    /// </summary>
    [ObservableProperty]
    private bool _isSelected = true;

    /// <summary>
    /// Gets or sets a value indicating whether the item selection is mandatory.
    /// </summary>
    [ObservableProperty]
    private bool _isMandatory;

    /// <summary>
    /// Gets or sets the display status (e.g., "Installed", "Missing").
    /// </summary>
    [ObservableProperty]
    private string _status = string.Empty;

    /// <summary>
    /// Gets or sets the label for the action button/toggle (e.g., "Install", "Update").
    /// </summary>
    [ObservableProperty]
    private string _actionLabel = string.Empty;

    /// <summary>
    /// Gets or sets the path to the icon image.
    /// </summary>
    [ObservableProperty]
    private string _iconPath = string.Empty;

    /// <summary>
    /// Gets or sets the version string to display.
    /// </summary>
    [ObservableProperty]
    private string _version = string.Empty;

    /// <summary>
    /// Gets or sets the type of action to perform (e.g., Install, Update, CreateProfile).
    /// </summary>
    public WizardActionType ActionType { get; set; } = WizardActionType.None;

    /// <summary>
    /// Gets or sets the GameInstallation context associated with this item.
    /// </summary>
    public GameInstallation? Installation { get; set; }

    /// <summary>
    /// Gets or sets additional metadata required for processing (e.g., PublisherType).
    /// </summary>
    public object? Metadata { get; set; }
}
