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
    public string LibraryGameName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(LibraryProfile.GameClient?.Name))
            {
                return LibraryProfile.GameClient.Name;
            }

            if (!string.IsNullOrWhiteSpace(SourceTypeName))
            {
                return SourceTypeName;
            }

            return GetLibraryText("GameProfiles.Library.UnknownGame", "Game profile");
        }
    }

    /// <summary>
    /// Gets the display version, retaining intentional suppression of local/default versions.
    /// </summary>
    public string LibraryVersion => GameVersion ?? Version;

    /// <summary>
    /// Gets the user's description, with existing derived metadata as a fallback.
    /// </summary>
    public string LibraryDescription
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(LibraryProfile.Description))
            {
                return LibraryProfile.Description;
            }

            if (!string.IsNullOrWhiteSpace(Description))
            {
                return Description;
            }

            return GetLibraryText("GameProfiles.Library.NoDescription", "No description has been added to this profile.");
        }
    }

    /// <summary>
    /// Gets the current process/preparation state without claiming file validation.
    /// </summary>
    public string LibraryLaunchStatus
    {
        get
        {
            if (IsProcessRunning)
            {
                return GetLibraryText("GameProfiles.Library.Running", "Running");
            }

            if (IsPreparingWorkspace)
            {
                return GetLibraryText("GameProfiles.Library.Preparing", "Preparing workspace…");
            }

            return GetLibraryText("GameProfiles.Library.LaunchAvailable", "Available to launch");
        }
    }

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
        NotifyLibraryCaptionsChanged();
        OnPropertyChanged(nameof(LibraryVersion));
        OnPropertyChanged(nameof(LibraryContentCount));
        OnPropertyChanged(nameof(LibraryProfile));
    }

    /// <summary>
    /// Refreshes localized captions without signaling a saved profile change.
    /// </summary>
    internal void NotifyLibraryCaptionsChanged()
    {
        OnPropertyChanged(nameof(LibraryGameName));
        OnPropertyChanged(nameof(LibraryDescription));
        OnPropertyChanged(nameof(LibraryLaunchStatus));
        OnPropertyChanged(nameof(LibraryLaunchMethod));
        OnPropertyChanged(nameof(LibraryArguments));
    }

    private static string GetLibraryText(string key, string fallback)
    {
        return LocalizationConverterHelper.GetLocalizedOrDefault(
            LocalizationConverterHelper.ResolveLocalizationService(),
            key,
            fallback);
    }
}
