using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Models.GameProfile;
using GenHub.Infrastructure.Converters;

namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// Read-only profile metadata for the library detail pane.
/// </summary>
public partial class GameProfileItemViewModel
{
    private IGameProfile? _libraryProfile;

    /// <summary>
    /// Gets the game name without replacing the user's profile name.
    /// </summary>
    public string LibraryGameName => !string.IsNullOrWhiteSpace(LibraryProfile.GameClient?.Name)
        ? LibraryProfile.GameClient.Name
        : !string.IsNullOrWhiteSpace(SourceTypeName)
            ? SourceTypeName
            : GetLibraryText("GameProfiles.Library.UnknownGame", "Game profile");

    /// <summary>
    /// Gets the display version, retaining intentional suppression of local/default versions.
    /// </summary>
    public string LibraryVersion => GameVersion ?? Version;

    /// <summary>
    /// Gets the user's description, with existing derived metadata as a fallback.
    /// </summary>
    public string LibraryDescription => !string.IsNullOrWhiteSpace(LibraryProfile.Description)
        ? LibraryProfile.Description
        : !string.IsNullOrWhiteSpace(Description)
            ? Description
            : GetLibraryText("GameProfiles.Library.NoDescription", "No description has been added to this profile.");

    /// <summary>
    /// Gets the current process/preparation state without claiming file validation.
    /// </summary>
    public string LibraryLaunchStatus => IsProcessRunning
        ? GetLibraryText("GameProfiles.Library.Running", "Running")
        : IsPreparingWorkspace
            ? GetLibraryText("GameProfiles.Library.Preparing", "Preparing workspace…")
            : GetLibraryText("GameProfiles.Library.LaunchAvailable", "Available to launch");

    /// <summary>
    /// Gets the effective launch mode shown beside the launch action.
    /// </summary>
    public string LibraryLaunchMethod => UseSteamLaunch
        ? GetLibraryText("GameProfiles.Library.SteamLaunch", "Launch through Steam")
        : GetLibraryText("GameProfiles.Library.LocalLaunch", "Local launch");

    /// <summary>
    /// Gets the saved launch arguments, or a readable empty-state caption.
    /// </summary>
    public string LibraryArguments
    {
        get
        {
            var arguments = LibraryProfile is GameProfile profile ? profile.CommandLineArguments : CommandLineArguments;
            return !string.IsNullOrWhiteSpace(arguments)
                ? arguments
                : GetLibraryText("GameProfiles.Library.NoArguments", "No extra arguments");
        }
    }

    /// <summary>
    /// Gets the number of enabled content entries in the saved profile.
    /// </summary>
    public int LibraryContentCount => LibraryProfile.EnabledContentIds?.Count ?? 0;

    /// <summary>
    /// Gets the latest profile snapshot for loading the library's content metadata.
    /// </summary>
    internal IGameProfile LibraryProfile => _libraryProfile ?? Profile;

    /// <summary>
    /// Refreshes captions when saved profile data or application language changes.
    /// </summary>
    internal void NotifyLibraryPropertiesChanged()
    {
        OnPropertyChanged(nameof(LibraryGameName));
        OnPropertyChanged(nameof(LibraryVersion));
        OnPropertyChanged(nameof(LibraryDescription));
        OnPropertyChanged(nameof(LibraryLaunchStatus));
        OnPropertyChanged(nameof(LibraryLaunchMethod));
        OnPropertyChanged(nameof(LibraryArguments));
        OnPropertyChanged(nameof(LibraryContentCount));
        OnPropertyChanged(nameof(LibraryProfile));
    }

    private static string GetLibraryText(string key, string fallback)
    {
        return LocalizationConverterHelper.GetLocalizedOrDefault(
            LocalizationConverterHelper.ResolveLocalizationService(),
            key,
            fallback);
    }
}
