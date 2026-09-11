using System.ComponentModel;
using System.Runtime.CompilerServices;
using GenHub.Core.Models.Enums;

namespace GenHub.Features.Downloads.ViewModels;

/// <summary>
/// Represents a specific installable variant for multi-asset content.
/// </summary>
public class InstallableVariant : INotifyPropertyChanged
{
    private ContentState _currentState = ContentState.NotDownloaded;

    /// <summary>
    /// Gets or sets the display name of the variant.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the specific manifest ID for this variant.
    /// </summary>
    public string ManifestId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the command to add this variant to a profile.
    /// Expects a GameProfile as parameter.
    /// </summary>
    public System.Windows.Input.ICommand? AddToProfileCommand { get; set; }

    /// <summary>
    /// Gets or sets the icon URL for this variant (usually matches the main item).
    /// </summary>
    public string IconUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the variant axis (e.g. "resolution", "language", "game-type").
    /// Empty means untyped — treated as a single default axis in the UI.
    /// </summary>
    public string VariantType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the current install state of this variant.
    /// </summary>
    public ContentState CurrentState
    {
        get => _currentState;
        set
        {
            if (_currentState != value)
            {
                _currentState = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StateDisplayText));
            }
        }
    }

    /// <summary>
    /// Gets a human-readable label for the variant's current install state.
    /// </summary>
    public string StateDisplayText => CurrentState switch
    {
        ContentState.Downloaded => "Installed",
        ContentState.UpdateAvailable => "Update",
        _ => "Not Installed",
    };

    /// <summary>
    /// Occurs when a property value changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Raises the <see cref="PropertyChanged"/> event.
    /// </summary>
    /// <param name="propertyName">Name of the property that changed.</param>
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
