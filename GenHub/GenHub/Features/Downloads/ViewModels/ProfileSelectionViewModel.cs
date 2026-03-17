using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
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
/// <param name="manifestPool">The content manifest pool.</param>
/// <param name="notificationService">The notification service.</param>
public sealed partial class ProfileSelectionViewModel(
    ILogger<ProfileSelectionViewModel> logger,
    IGameProfileManager profileManager,
    IProfileContentService profileContentService,
    IContentManifestPool manifestPool,
    INotificationService notificationService) : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<ProfileOptionViewModel> _compatibleProfiles = [];

    [ObservableProperty]
    private ObservableCollection<ProfileOptionViewModel> _otherProfiles = [];

    /// <summary>
    /// Gets a value indicating whether there are other profiles with warnings.
    /// </summary>
    public bool HasOtherProfiles => OtherProfiles.Count > 0;

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
    /// Gets a value indicating whether there are compatible profiles available.
    /// </summary>
    public bool HasCompatibleProfiles => CompatibleProfiles.Count > 0;

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
    /// <returns>A task representing the asynchronous operation.</returns>
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
                    var profileGameType = profile.GameClient?.GameType.ToString() ?? "Tool";
                    option.WarningMessage = $"This profile is for {profileGameType}, content is for {targetGame}";
                    OtherProfiles.Add(option);
                }
            }

            OnPropertyChanged(nameof(HasAnyProfiles));
            OnPropertyChanged(nameof(HasCompatibleProfiles));
            OnPropertyChanged(nameof(HasOtherProfiles));
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
    /// <returns>True if compatible, otherwise false.</returns>
    private static bool IsCompatible(GameProfile profile, GameType targetGame)
    {
        // Tool profiles (with null GameClient) are not compatible with game content
        if (profile.GameClient == null)
        {
            return false;
        }

        // ZeroHour content can only go in ZeroHour profiles
        // Generals content can only go in Generals profiles
        return profile.GameClient.GameType == targetGame;
    }

    /// <summary>
    /// Selects a profile and optionally adds content to it.
    /// </summary>
    /// <param name="option">The selected profile option.</param>
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

                    // Show success notification for new content addition
                    notificationService.ShowSuccess(
                        "Content Added",
                        $"Added '{ContentName}' to profile '{profile.Name}'");
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
    /// If the content has multiple variants, shows variant selection dialog first.
    /// </summary>
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

            // Check if this content has variants
            var manifestResult = await manifestPool.GetManifestAsync(
                ManifestId.Create(ContentManifestId),
                CancellationToken.None);

            string selectedManifestId = ContentManifestId;
            string selectedContentName = ContentName ?? "New Profile";

            if (manifestResult.Success && manifestResult.Data != null)
            {
                var manifest = manifestResult.Data;

                // Check if there are variants by looking for related manifests with different game types
                var allManifestsResult = await manifestPool.GetAllManifestsAsync(CancellationToken.None);
                if (allManifestsResult.Success && allManifestsResult.Data != null)
                {
                    // Find variants: same publisher, same name, different game type
                    var variants = allManifestsResult.Data
                        .Where(m =>
                            m.Publisher.Name == manifest.Publisher.Name &&
                            m.Name == manifest.Name &&
                            m.TargetGame != manifest.TargetGame)
                        .ToList();

                    if (variants.Count > 0)
                    {
                        // Add the current manifest to the list
                        var allVariants = new ObservableCollection<ContentManifest> { manifest };
                        foreach (var variant in variants)
                        {
                            allVariants.Add(variant);
                        }

                        // Show variant selection dialog
                        var variantLogger = logger as ILogger<VariantSelectionViewModel>
                            ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<VariantSelectionViewModel>.Instance;
                        var variantViewModel = new VariantSelectionViewModel(
                            variantLogger,
                            manifest.Name,
                            allVariants);

                        // TODO: Refactor to use proper MVVM pattern with view resolution service instead of direct View instantiation
                        var variantDialog = new Views.VariantSelectionView(variantViewModel);

                        var currentWindow = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                            ? desktop.MainWindow
                            : null;

                        if (currentWindow != null)
                        {
                            await variantDialog.ShowDialog(currentWindow);

                            if (!variantViewModel.WasSuccessful || variantViewModel.SelectedVariant == null)
                            {
                                logger.LogInformation("Variant selection cancelled");
                                return;
                            }

                            selectedManifestId = variantViewModel.SelectedVariant.ManifestId;
                            selectedContentName = variantViewModel.SelectedVariant.Name;
                            logger.LogInformation("Selected variant: {VariantName} ({ManifestId})", selectedContentName, selectedManifestId);
                        }
                    }
                }
            }

            var baseName = string.IsNullOrEmpty(selectedContentName) ? "New Profile" : $"{selectedContentName} Profile";
            var profileName = baseName;
            var counter = 1;

            // Ensure unique name
            while (await ProfileExistsAsync(profileName))
            {
                profileName = $"{baseName} ({counter++})";
            }

            var result = await profileContentService.CreateProfileWithContentAsync(
                profileName,
                selectedManifestId,
                CancellationToken.None);

            if (result.Success && result.Data != null)
            {
                logger.LogInformation("Successfully created profile '{ProfileName}'", result.Data.Name);

                notificationService.ShowSuccess(
                    "Profile Created",
                    $"Created profile '{result.Data.Name}' with {selectedContentName}");

                // Refresh the profile list
                await LoadProfilesAsync(TargetGame, selectedManifestId, selectedContentName);
            }
            else
            {
                logger.LogError(
                    "Failed to create profile: {Error}",
                    result.FirstError ?? "Unknown error");

                notificationService.ShowError(
                    "Profile Creation Failed",
                    result.FirstError ?? "Unknown error");
            }
        }
        catch (System.Exception ex)
        {
            logger.LogError(ex, "Exception creating profile with content");
            notificationService.ShowError(
                "Profile Creation Failed",
                $"An error occurred: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks if a profile with the given name already exists.
    /// </summary>
    /// <param name="profileName">The profile name to check.</param>
    /// <returns>True if a profile exists, otherwise false.</returns>
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
