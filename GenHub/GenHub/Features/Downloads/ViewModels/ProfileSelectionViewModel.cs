using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Downloads.ViewModels;

/// <summary>
/// ViewModel for profile selection with smart filtering.
/// Shows compatible profiles first, then other profiles with warnings.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ProfileSelectionViewModel"/> class.
/// </remarks>
/// <param name="logger">The logger.</param>
/// <param name="profileManager">The profile manager.</param>
/// <param name="profileContentService">The profile content service.</param>
public sealed partial class ProfileSelectionViewModel(
    ILogger<ProfileSelectionViewModel> logger,
    IGameProfileManager profileManager,
    IProfileContentService profileContentService) : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<ProfileOptionViewModel> _compatibleProfiles = [];

    [ObservableProperty]
    private ObservableCollection<ProfileOptionViewModel> _otherProfiles = [];

    [ObservableProperty]
    private GameType _targetGame;

    [ObservableProperty]
    private string? _contentManifestId;

    [ObservableProperty]
    private string? _contentName;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _wasSuccessful;

    [ObservableProperty]
    private string? _selectedProfileName;

    /// <summary>
    /// Event raised when the dialog should be closed.
    /// </summary>
    public event EventHandler? RequestClose;

    /// <summary>
    /// Gets a value indicating whether there are any profiles available.
    /// </summary>
    public bool HasAnyProfiles => CompatibleProfiles.Count > 0 || OtherProfiles.Count > 0;

    /// <summary>
    /// Gets a summary of the profile counts.
    /// </summary>
    public string ProfileSummary
    {
        get
        {
            var compatibleCount = CompatibleProfiles.Count;
            var otherCount = OtherProfiles.Count;

            if (compatibleCount == 0 && otherCount == 0)
            {
                return "No profiles available";
            }

            if (otherCount == 0)
            {
                return compatibleCount == 1 ? "1 compatible profile" : $"{compatibleCount} compatible profiles";
            }

            if (compatibleCount == 0)
            {
                return otherCount == 1 ? "1 incompatible profile" : $"{otherCount} incompatible profiles";
            }

            return $"{compatibleCount} compatible, {otherCount} incompatible";
        }
    }

    /// <summary>
    /// Filters profiles by compatibility with target game.
    /// </summary>
    /// <param name="targetGame">The target game type for compatibility.</param>
    /// <param name="contentManifestId">The optional content manifest ID to be added.</param>
    /// <param name="contentName">The optional content name for display.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <summary>
    /// Loads all profiles, partitions them into profiles compatible with the specified target game and incompatible profiles, and updates the view model state accordingly.
    /// </summary>
    /// <param name="contentManifestId">Optional content manifest identifier to associate with profiles when selecting or creating profiles.</param>
    /// <param name="contentName">Optional display name of the content used to derive new profile names when creating a profile.</param>
    /// <param name="ct">Cancellation token to cancel the load operation.</param>
    /// <returns>Completes after the view model's profile lists and related state (TargetGame, ContentManifestId, ContentName, CompatibleProfiles, OtherProfiles, HasAnyProfiles, ProfileSummary, IsLoading, and ErrorMessage) have been updated.</returns>
    public async Task LoadProfilesAsync(
        GameType targetGame,
        string? contentManifestId = null,
        string? contentName = null,
        CancellationToken ct = default)
    {
        TargetGame = targetGame;
        ContentManifestId = contentManifestId;
        ContentName = contentName;
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var profilesResult = await profileManager.GetAllProfilesAsync(ct);

            if (!profilesResult.Success || profilesResult.Data == null)
            {
                ErrorMessage = profilesResult.Errors != null ? string.Join(", ", profilesResult.Errors) : "Failed to load profiles";
                logger.LogWarning("Failed to load profiles: {Error}", ErrorMessage);
                return;
            }

            CompatibleProfiles.Clear();
            OtherProfiles.Clear();

            foreach (var profile in profilesResult.Data)
            {
                var option = new ProfileOptionViewModel(profile);

                // Check if profile's game type matches content's target game
                if (IsCompatible(profile, targetGame))
                {
                    CompatibleProfiles.Add(option);
                }
                else
                {
                    option.ShowWarning = true;
                    option.WarningMessage = $"This profile is for {profile.GameClient.GameType}, content is for {targetGame}";
                    OtherProfiles.Add(option);
                }
            }

            OnPropertyChanged(nameof(HasAnyProfiles));
            OnPropertyChanged(nameof(ProfileSummary));

            logger.LogInformation(
                "Loaded {CompatibleCount} compatible and {OtherCount} incompatible profiles for {TargetGame}",
                CompatibleProfiles.Count,
                OtherProfiles.Count,
                targetGame);
        }
        catch (System.OperationCanceledException)
        {
            // Ignore cancellation
        }
        catch (System.Exception ex)
        {
            ErrorMessage = $"Failed to load profiles: {ex.Message}";
            logger.LogError(ex, "Failed to load profiles");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Determines if a profile is compatible with the target game type.
    /// </summary>
    /// <param name="profile">The profile to check.</param>
    /// <param name="targetGame">The target game type.</param>
    /// <summary>
    /// Determines whether the given profile is compatible with the specified target game.
    /// </summary>
    /// <param name="profile">The profile to evaluate.</param>
    /// <param name="targetGame">The game type to compare against.</param>
    /// <returns>`true` if the profile's game type equals the target game, `false` otherwise.</returns>
    private static bool IsCompatible(GameProfile profile, GameType targetGame)
    {
        // ZeroHour content can only go in ZeroHour profiles
        // Generals content can only go in Generals profiles
        return profile.GameClient.GameType == targetGame;
    }

    /// <summary>
    /// Selects a profile and optionally adds content to it.
    /// </summary>
    /// <summary>
    /// Selects the provided profile option; if content is specified on the view model, attempts to add that content to the profile, then requests the dialog to close.
    /// </summary>
    /// <param name="option">The profile option to select; if null the selection is ignored.</param>
    /// <remarks>
    /// On success sets <see cref="SelectedProfileName"/> and <see cref="WasSuccessful"/> to true. On failure sets <see cref="ErrorMessage"/> and <see cref="WasSuccessful"/> to false. Always raises <see cref="RequestClose"/> when finished.
    /// </remarks>
    [RelayCommand]
    private async Task SelectProfileAsync(ProfileOptionViewModel? option)
    {
        if (option == null)
        {
            logger.LogWarning("No profile selected");
            return;
        }

        var profile = option.Profile;

        try
        {
            if (string.IsNullOrEmpty(ContentManifestId))
            {
                logger.LogInformation("Profile '{ProfileName}' selected (no content to add)", profile.Name);
                SelectedProfileName = profile.Name;
                WasSuccessful = true;
                RequestClose?.Invoke(this, EventArgs.Empty);
                return;
            }

            logger.LogInformation(
                "Adding content '{ContentName}' ({ContentId}) to profile '{ProfileName}'",
                ContentName,
                ContentManifestId,
                profile.Name);

            var result = await profileContentService.AddContentToProfileAsync(
                profile.Id,
                ContentManifestId,
                CancellationToken.None);

            if (result.Success)
            {
                if (result.WasContentSwapped)
                {
                    logger.LogInformation(
                        "Content swap: replaced {OldContent} with {NewContent} in profile {ProfileName}",
                        result.SwappedContentName,
                        ContentName,
                        profile.Name);
                }
                else
                {
                    logger.LogInformation("Successfully added content to profile '{ProfileName}'", profile.Name);
                }

                SelectedProfileName = profile.Name;
                WasSuccessful = true;
            }
            else
            {
                logger.LogError(
                    "Failed to add content to profile '{ProfileName}': {Error}",
                    profile.Name,
                    result.FirstError);
                ErrorMessage = result.FirstError ?? "Failed to add content to profile";
                WasSuccessful = false;
            }
        }
        catch (System.Exception ex)
        {
            logger.LogError(ex, "Error adding content to profile '{ProfileName}'", profile.Name);
            ErrorMessage = ex.Message;
            WasSuccessful = false;
        }

        // Close the dialog after the operation
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Cancels the profile selection and closes the dialog.
    /// <summary>
    /// Cancels the selection and requests the dialog to close without selecting a profile.
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        WasSuccessful = false;
        SelectedProfileName = null;
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Creates a new profile with the current content pre-enabled.
    /// <summary>
    /// Creates a new profile pre-populated with the current content manifest and refreshes the profile list on success.
    /// </summary>
    /// <remarks>
    /// If <see cref="ContentManifestId"/> is null or empty the method exits without action. The method derives a base name from <see cref="ContentName"/>, ensures the profile name is unique by appending " (n)" when necessary, and calls the content service to create the profile. On successful creation the profile list is reloaded for <see cref="TargetGame"/>; failures are logged but not thrown. Exceptions are caught and logged. This method is executed as an async command.
    /// </remarks>
    [RelayCommand]
    private async Task CreateNewProfileAsync()
    {
        if (string.IsNullOrEmpty(ContentManifestId))
        {
            logger.LogWarning("Cannot create profile: no content manifest ID provided");
            return;
        }

        try
        {
            logger.LogInformation(
                "Creating new profile for content '{ContentName}' ({ContentId})",
                ContentName ?? "Unknown",
                ContentManifestId);

            var baseName = string.IsNullOrEmpty(ContentName) ? "New Profile" : $"{ContentName} Profile";
            var profileName = baseName;
            var counter = 1;

            // Ensure unique name
            while (await ProfileExistsAsync(profileName))
            {
                profileName = $"{baseName} ({counter++})";
            }

            var result = await profileContentService.CreateProfileWithContentAsync(
                profileName,
                ContentManifestId,
                CancellationToken.None);

            if (result.Success && result.Data != null)
            {
                logger.LogInformation("Successfully created profile '{ProfileName}'", result.Data.Name);

                // Refresh the profile list
                await LoadProfilesAsync(TargetGame, ContentManifestId, ContentName);
            }
            else
            {
                logger.LogError(
                    "Failed to create profile: {Error}",
                    result.FirstError ?? "Unknown error");
            }
        }
        catch (System.Exception ex)
        {
            logger.LogError(ex, "Exception creating profile with content");
        }
    }

    /// <summary>
    /// Checks if a profile with the given name already exists.
    /// </summary>
    /// <param name="profileName">The profile name to check.</param>
    /// <summary>
    /// Determines whether a profile with the specified name exists.
    /// </summary>
    /// <param name="profileName">The profile name to check; comparison is case-insensitive.</param>
    /// <returns>`true` if a profile with the given name exists, `false` otherwise.</returns>
    private async Task<bool> ProfileExistsAsync(string profileName)
    {
        var profilesResult = await profileManager.GetAllProfilesAsync(CancellationToken.None);
        if (profilesResult.Success && profilesResult.Data != null)
        {
            return profilesResult.Data.Any(p =>
                string.Equals(p.Name, profileName, StringComparison.OrdinalIgnoreCase));
        }

        return false;
    }
}