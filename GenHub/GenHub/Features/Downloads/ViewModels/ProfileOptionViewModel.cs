using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;

namespace GenHub.Features.Downloads.ViewModels;

/// <summary>
/// Represents a profile option in the selection list.
/// </summary>
public sealed partial class ProfileOptionViewModel(GameProfile profile) : ObservableObject
{
    /// <summary>
    /// Gets the underlying game profile.
    /// </summary>
    public GameProfile Profile { get; } = profile;

    /// <summary>
    /// Gets the profile name.
    /// </summary>
    public string Name => Profile.Name ?? "Unnamed Profile";

    /// <summary>
    /// Gets the game type.
    /// </summary>
    public GameType GameType => Profile.GameClient.GameType;

    /// <summary>
    /// Gets the game client name.
    /// </summary>
    public string GameClientName => Profile.GameClient?.Name ?? "Unknown Client";

    /// <summary>
    /// Gets or sets a value indicating whether a warning should be shown.
    /// </summary>
    [ObservableProperty]
    private bool _showWarning;

    /// <summary>
    /// Gets or sets the warning message.
    /// </summary>
    [ObservableProperty]
    private string? _warningMessage;

    /// <summary>
    /// Gets the description for display.
    /// </summary>
    public string Description => string.IsNullOrEmpty(Profile.Description)
        ? GameClientName
        : Profile.Description;
}
